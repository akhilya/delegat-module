using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Delegat.Enums;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Mapping;

namespace Delegat.Services.Ai;

/// <summary>
/// Phase 1 of the new semantic compatibility flow.
/// Performs deterministic scoring of local attributes vs adapter fields
/// to produce a CompatibilityReport (clear matches, ambiguities, gaps, risks)
/// and a small set of clarifying questions (3-5 max) for the user.
/// 
/// Goal: only propose semantically compatible pairs. Prefer leaving fields empty
/// over making a plausible-but-wrong connection.
/// </summary>
public class MappingCompatibilityService
{
    private readonly DataContext _context;
    private readonly MappingIntelligenceService _intelligence;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly ILogger<MappingCompatibilityService> _logger;

    // Scoring thresholds (as proposed in plan)
    private const double ClearMatchThreshold = 0.85;
    private const double QuestionThreshold = 0.60;
    private const double MinViableScore = 0.55;

    public MappingCompatibilityService(
        DataContext context,
        MappingIntelligenceService intelligence,
        IMarketplaceAdapterRegistry adapterRegistry,
        ILogger<MappingCompatibilityService> logger)
    {
        _context = context;
        _intelligence = intelligence;
        _adapterRegistry = adapterRegistry;
        _logger = logger;
    }

    /// <summary>
    /// Main entry: analyze a mapping for semantic compatibility.
    /// Loads local attributes + adapter schema and runs scoring.
    /// </summary>
    public async Task<CompatibilityAnalysisResult> AnalyzeAsync(
        int mappingId,
        int? previewProductId = null,
        CancellationToken ct = default)
    {
        var mapping = await _context.categoryMappings
            .AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == mappingId, ct);
        if (mapping == null)
            throw new InvalidOperationException($"Mapping {mappingId} not found");

        var localAttributes = await _context.categoryAttributes
            .AsNoTracking()
            .Where(a => a.categoryId == mapping.localCategoryId)
            .OrderBy(a => a.sortOrder)
            .ToListAsync(ct);

        var adapter = _adapterRegistry.GetByKey(mapping.connection.adapterKey)
            ?? throw new InvalidOperationException($"Adapter {mapping.connection.adapterKey} not found");

        var adapterFields = await adapter.GetCategoryFieldsAsync(
            mapping.connection, mapping.externalCategoryId, ct);

        var schemaContext = await _intelligence.GetSchemaContextAsync(mapping, ct);
        var productSample = await _intelligence.GetProductSampleAsync(
            mapping.localCategoryId, previewProductId, ct);

        // Precompute option hints for cross-marketplace (reuse existing intelligence)
        var alignmentHints = await _intelligence.BuildAlignmentHintsAsync(
            mapping, adapterFields, previewProductId, ct);

        var report = BuildCompatibilityReport(
            mapping, localAttributes, adapterFields, schemaContext, alignmentHints);

        var questions = GenerateQuestions(report, localAttributes, adapterFields, schemaContext, 5);

        return new CompatibilityAnalysisResult
        {
            Report = report,
            Questions = questions,
            MappingId = mappingId,
            AnalyzedAt = DateTime.UtcNow,
            SchemaContextHint = schemaContext.SchemaSource?.Hint
        };
    }

    /// <summary>
    /// Pure deterministic scoring pass. Produces the structured report.
    /// </summary>
    private CompatibilityReport BuildCompatibilityReport(
        CategoryMapping mapping,
        List<CategoryAttribute> localAttributes,
        IReadOnlyList<ExternalField> adapterFields,
        SchemaContextDto schemaContext,
        List<OptionAlignmentHintDto> alignmentHints)
    {
        var report = new CompatibilityReport
        {
            MappingId = mapping.Id,
            LocalCategoryId = mapping.localCategoryId,
            AdapterKey = mapping.connection.adapterKey,
            SchemaSource = schemaContext.SchemaSource != null ? new CrossMarketplaceInfo
            {
                FromConnectionName = schemaContext.SchemaSource.ConnectionName,
                FromAdapterKey = schemaContext.SchemaSource.AdapterKey,
                Hint = schemaContext.SchemaSource.Hint
            } : null
        };

        var usedLocalAttrIds = new HashSet<int>(); // cardinality: one local -> one primary target (for now)
        var usedAdapterKeys = new HashSet<string>();

        foreach (var field in adapterFields.OrderBy(f => f.IsRequired ? 0 : 1).ThenBy(f => f.SortOrder))
        {
            var candidates = ScoreCandidates(field, localAttributes, schemaContext, alignmentHints);

            if (candidates.Count == 0)
            {
                report.AdapterOnly.Add(new AdapterOnlyField
                {
                    Key = field.Key,
                    Name = field.Name,
                    Type = field.Type,
                    IsRequired = field.IsRequired,
                    Unit = field.Unit,
                    Reason = "Нет локального атрибута с совместимой семантикой, типом или словарём"
                });
                continue;
            }

            // Best candidate
            var best = candidates.OrderByDescending(c => c.Score).First();

            // Clear match? Standard product fields (sku, images, …) use a slightly lower bar.
            var isStandardProductField = best.SourceType == "productField" && best.Score >= 0.80;
            if ((best.Score >= ClearMatchThreshold || isStandardProductField)
                && !usedLocalAttrIds.Contains(best.SourceAttributeId ?? -1))
            {
                if (!IsSemanticallySafe(field, best, localAttributes))
                {
                    report.Incompatible.Add(new IncompatiblePair
                    {
                        AdapterFieldKey = field.Key,
                        AdapterName = field.Name,
                        Candidate = best,
                        Reason = "Несовместимость по типу/единицам/направлению (риск потери данных)"
                    });
                    continue;
                }

                report.ClearMatches.Add(new ClearMatch
                {
                    AdapterFieldKey = field.Key,
                    AdapterFieldName = field.Name,
                    IsRequired = field.IsRequired,
                    IsAspect = field.IsAspect,
                    BestSource = best,
                    Score = best.Score
                });

                if (best.SourceAttributeId.HasValue)
                    usedLocalAttrIds.Add(best.SourceAttributeId.Value);
                usedAdapterKeys.Add(field.Key);
                continue;
            }

            // Ambiguous or low-score
            var topCandidates = candidates
                .Where(c => c.Score >= QuestionThreshold)
                .OrderByDescending(c => c.Score)
                .Take(3)
                .ToList();

            if (topCandidates.Count >= 2 && topCandidates[0].Score - topCandidates[1].Score < 0.15)
            {
                report.AmbiguousPairs.Add(new AmbiguousPair
                {
                    AdapterFieldKey = field.Key,
                    AdapterFieldName = field.Name,
                    IsRequired = field.IsRequired,
                    Candidates = topCandidates,
                    Reason = "Несколько близких по скорингу кандидатов — требуется уточнение смысла данных"
                });
            }
            else if (best.Score >= QuestionThreshold && !usedLocalAttrIds.Contains(best.SourceAttributeId ?? -1))
            {
                var borderlineCandidates = topCandidates.Count > 0
                    ? topCandidates
                    : new List<CompatibilityCandidate> { best };

                // Single obvious name match — no need to bother the user
                if (IsObviousSingleMatch(field.Name, field.Key, borderlineCandidates)
                    && IsSemanticallySafe(field, best, localAttributes))
                {
                    report.ClearMatches.Add(new ClearMatch
                    {
                        AdapterFieldKey = field.Key,
                        AdapterFieldName = field.Name,
                        IsRequired = field.IsRequired,
                        IsAspect = field.IsAspect,
                        BestSource = best,
                        Score = best.Score
                    });

                    if (best.SourceAttributeId.HasValue)
                        usedLocalAttrIds.Add(best.SourceAttributeId.Value);
                    usedAdapterKeys.Add(field.Key);
                    continue;
                }

                // Borderline — treat as required gap if required
                if (field.IsRequired)
                {
                    report.RequiredGaps.Add(new RequiredGap
                    {
                        AdapterFieldKey = field.Key,
                        AdapterFieldName = field.Name,
                        TopCandidate = best,
                        Score = best.Score,
                        Reason = "Обязательное поле без высокоточного совпадения"
                    });
                }
                else
                {
                    report.AmbiguousPairs.Add(new AmbiguousPair
                    {
                        AdapterFieldKey = field.Key,
                        AdapterFieldName = field.Name,
                        IsRequired = field.IsRequired,
                        Candidates = borderlineCandidates,
                        Reason = "Средняя уверенность — рекомендуется уточнить"
                    });
                }
            }
            else
            {
                // Low score or conflict
                report.AdapterOnly.Add(new AdapterOnlyField
                {
                    Key = field.Key,
                    Name = field.Name,
                    Type = field.Type,
                    IsRequired = field.IsRequired,
                    Reason = $"Лучший кандидат имеет низкий score ({best.Score:F2}) — безопаснее оставить пустым"
                });
            }
        }

        // Local-only attributes (not forced)
        var matchedAttrIds = report.ClearMatches
            .Where(m => m.BestSource.SourceAttributeId.HasValue)
            .Select(m => m.BestSource.SourceAttributeId!.Value)
            .ToHashSet();

        foreach (var attr in localAttributes.Where(a => !matchedAttrIds.Contains(a.Id)))
        {
            report.LocalOnly.Add(new LocalOnlyAttribute
            {
                AttributeId = attr.Id,
                Name = attr.name,
                Key = attr.key,
                Type = attr.type.ToString(),
                Reason = "Нет подходящего поля адаптера с той же семантикой"
            });
        }

        // Cross-marketplace risks (select fields with hints)
        foreach (var hint in alignmentHints.Where(h => h.MatchScore < 80 && h.ValueMapMappings.Count > 0))
        {
            report.CrossMarketplaceRisks.Add(new CrossMarketplaceRisk
            {
                AdapterFieldKey = hint.AdapterFieldKey,
                SourceAttributeId = hint.SourceAttributeId,
                MatchScore = hint.MatchScore,
                UnmappedLocalCount = hint.UnmappedLocalCount,
                Note = hint.Note ?? "Низкое перекрытие словарей при кросс-маркетплейс импорте"
            });
        }

        // Compute simple overall score (weighted, normalized to 0-1)
        var totalFields = adapterFields.Count;
        var requiredTotal = adapterFields.Count(f => f.IsRequired);
        var requiredCovered = report.ClearMatches.Count(m => m.IsRequired);

        var clearRatio = totalFields > 0 ? (double)report.ClearMatches.Count / totalFields : 0;
        var requiredRatio = requiredTotal > 0 ? (double)requiredCovered / requiredTotal : 1.0;

        // Weighted: 50% clear matches coverage, 50% required coverage
        var raw = clearRatio * 0.5 + requiredRatio * 0.5;
        report.CompatibilityScore = Math.Min(1.0, Math.Round(raw, 3));

        report.Summary = new ReportSummary
        {
            TotalAdapterFields = totalFields,
            ClearMatches = report.ClearMatches.Count,
            Ambiguous = report.AmbiguousPairs.Count,
            IntentionallyLeftEmpty = report.AdapterOnly.Count,
            RequiredGaps = report.RequiredGaps.Count,
            RequiredCoveredHighConfidence = requiredCovered
        };

        return report;
    }

    /// <summary>
    /// Score all local attributes against one adapter field. Returns sorted candidates.
    /// </summary>
    private List<CompatibilityCandidate> ScoreCandidates(
        ExternalField field,
        List<CategoryAttribute> localAttributes,
        SchemaContextDto schemaContext,
        List<OptionAlignmentHintDto> alignmentHints)
    {
        var results = new List<CompatibilityCandidate>();

        var hint = alignmentHints.FirstOrDefault(h => h.AdapterFieldKey == field.Key);

        foreach (var attr in localAttributes)
        {
            double score = 0.0;
            var reasons = new List<string>();

            // 1. Name / semantic match (base)
            var nameScore = ComputeNameScore(attr, field);
            score += nameScore * 0.35;
            if (nameScore > 70) reasons.Add($"имя ({nameScore})");

            // 2. Type compatibility
            var typeScore = ComputeTypeScore(attr.type, field.Type);
            score += typeScore * 0.25;
            if (typeScore < 0.6) continue; // hard filter for bad type mismatch

            // 3. Group / description boost
            if (!string.IsNullOrWhiteSpace(attr.group) && !string.IsNullOrWhiteSpace(field.Group))
            {
                if (Normalize(attr.group).Contains(Normalize(field.Group)) || Normalize(field.Group).Contains(Normalize(attr.group)))
                {
                    score += 0.08;
                    reasons.Add("группа");
                }
            }
            if (!string.IsNullOrWhiteSpace(attr.description) && !string.IsNullOrWhiteSpace(field.Description))
            {
                if (HasSemanticOverlap(attr.description, field.Description))
                    score += 0.06;
            }

            // 4. Unit compatibility
            var unitScore = ComputeUnitScore(attr, field);
            score += unitScore * 0.12;
            if (unitScore > 0.7 && !string.IsNullOrEmpty(field.Unit)) reasons.Add($"unit {field.Unit}");

            // 5. Dictionary / select overlap (very important for compatibility)
            if ((attr.type == AttributeType.Select || attr.type == AttributeType.MultiSelect) &&
                field.Type is "select" or "multiselect")
            {
                var dictScore = ComputeDictionaryScore(attr, field, hint);
                score += dictScore * 0.20;
                if (dictScore > 0.6) reasons.Add("словарь");
            }

            // 6. Aspect preference
            if (field.IsAspect && (attr.name.Contains("цвет", StringComparison.OrdinalIgnoreCase) ||
                                   attr.name.Contains("размер", StringComparison.OrdinalIgnoreCase) ||
                                   attr.group?.Contains("вариац", StringComparison.OrdinalIgnoreCase) == true))
            {
                score += 0.08;
                reasons.Add("aspect");
            }

            // 7. Bidirectional safety penalty (critical fields)
            if (IsCriticalImportField(field.Key) && WouldLoseDataOnImport(attr, field))
            {
                score *= 0.6;
                reasons.Add("штраф bidirectional");
            }

            // 8. Cross-marketplace bonus/penalty from precomputed hints
            if (hint != null && hint.SourceAttributeId == attr.Id)
            {
                var hintBoost = Math.Min(0.15, hint.MatchScore / 100.0 * 0.15);
                score += hintBoost;
                if (hint.ValueMapMappings.Count > 0) reasons.Add("cross-hint");
            }

            // Normalize to 0-1
            score = Math.Min(1.0, Math.Max(0.0, score));

            if (score >= MinViableScore)
            {
                results.Add(new CompatibilityCandidate
                {
                    SourceType = "attribute",
                    SourceAttributeId = attr.Id,
                    SourceAttributeName = attr.name,
                    Score = Math.Round(score, 3),
                    Reason = reasons.Count > 0 ? string.Join("; ", reasons) : "семантическое совпадение"
                });
            }
        }

        // Also consider simple productField matches (sku, name, price, etc.) — lower weight than attributes usually
        var productFieldCandidates = TryProductFieldMatches(field);
        results.AddRange(productFieldCandidates);

        // Static value candidates only for very obvious constants (brand, country) — low priority in PR1
        if (IsObviousConstantField(field))
        {
            results.Add(new CompatibilityCandidate
            {
                SourceType = "static",
                StaticValue = InferObviousConstant(field),
                Score = 0.65,
                Reason = "очевидная константа категории"
            });
        }

        return results
            .OrderByDescending(c => c.Score)
            .Take(5)
            .ToList();
    }

    private static double ComputeNameScore(CategoryAttribute attr, ExternalField field)
    {
        var scores = new[]
        {
            ExactNormalizedMatch(attr.name, field.Name) ? 100 : 0,
            ExactNormalizedMatch(attr.name, field.Key) ? 92 : 0,
            ExactNormalizedMatch(attr.key, field.Key) ? 95 : 0,
            ExactNormalizedMatch(attr.key, field.Name) ? 88 : 0,
            PartialMatchScore(attr.name, field.Name),
            PartialMatchScore(attr.name, field.Key),
            PartialMatchScore(attr.key, field.Name),
            PartialMatchScore(attr.key, field.Key),
        };
        var baseScore = scores.Max();

        // Synonym boost (common marketplace terms)
        if (HasSynonymMatch(attr.name, field.Name) || HasSynonymMatch(attr.name, field.Key))
            baseScore = Math.Max(baseScore, 82);

        return baseScore / 100.0;
    }

    private static bool ExactNormalizedMatch(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        return Normalize(a) == Normalize(b);
    }

    private static int PartialMatchScore(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return 0;
        var na = Normalize(a);
        var nb = Normalize(b);
        if (na.Length < 3 || nb.Length < 3) return 0;
        if (na.Contains(nb) || nb.Contains(na)) return 72;
        // token overlap
        var ta = na.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
        var tb = nb.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
        var overlap = ta.Intersect(tb, StringComparer.Ordinal).Count();
        if (overlap >= 1 && (ta.Length + tb.Length) > 0)
            return (int)(60.0 * overlap / Math.Max(1, Math.Min(ta.Length, tb.Length)));
        return 0;
    }

    private static bool HasSynonymMatch(string? local, string? target)
    {
        if (string.IsNullOrWhiteSpace(local) || string.IsNullOrWhiteSpace(target)) return false;
        var l = Normalize(local);
        var t = Normalize(target);

        var synonyms = new (string[], string[])[]
        {
            (new[] { "название", "name", "title", "наим" }, new[] { "name", "title", "название" }),
            (new[] { "артикул", "sku", "article", "vendorcode" }, new[] { "article", "sku", "артикул" }),
            (new[] { "цена", "price", "cost" }, new[] { "price", "цена" }),
            (new[] { "вес", "weight", "масса" }, new[] { "weight", "вес" }),
            (new[] { "цвет", "color", "colour" }, new[] { "color", "цвет" }),
            (new[] { "размер", "size", "dimension" }, new[] { "size", "размер" }),
            (new[] { "описание", "description", "desc" }, new[] { "description", "описание" }),
            (new[] { "штрихкод", "barcode", "ean", "upc" }, new[] { "barcode", "штрихкод" }),
            (new[] { "бренд", "brand", "производитель" }, new[] { "brand", "бренд" }),
            (new[] { "страна", "country", "производства" }, new[] { "country", "страна" }),
        };

        foreach (var (locals, targets) in synonyms)
        {
            bool lMatch = locals.Any(s => l.Contains(s));
            bool tMatch = targets.Any(s => t.Contains(s));
            if (lMatch && tMatch) return true;
        }
        return false;
    }

    private static double ComputeTypeScore(AttributeType localType, string adapterType)
    {
        var at = adapterType.ToLowerInvariant();
        return (localType, at) switch
        {
            (AttributeType.Text, "string") => 1.0,
            (AttributeType.Number, "number") => 1.0,
            (AttributeType.Boolean, "boolean") => 1.0,
            (AttributeType.Select, "select") => 1.0,
            (AttributeType.MultiSelect, "multiselect") => 1.0,
            (AttributeType.Barcode, "string") => 0.95,
            (AttributeType.Text, "select") => 0.4,   // possible but lossy
            (AttributeType.Number, "string") => 0.65,
            (AttributeType.Text, "number") => 0.35,
            _ => 0.3
        };
    }

    private static double ComputeUnitScore(CategoryAttribute attr, ExternalField field)
    {
        if (string.IsNullOrEmpty(field.Unit)) return 0.85; // neutral if no unit declared

        var rawName = (attr.name + " " + (attr.description ?? "")).ToLowerInvariant();
        var unit = field.Unit.ToLowerInvariant().Trim();

        // Direct presence of the target unit in the name (e.g. "Длина, mm")
        if (rawName.Contains(unit) || rawName.Contains(" " + unit) || rawName.Contains("," + unit))
            return 1.0;

        // Common unit variants (both Cyrillic and Latin)
        var unitVariants = new Dictionary<string, string[]>
        {
            ["mm"] = new[] { "mm", "мм" },
            ["cm"] = new[] { "cm", "см" },
            ["m"]  = new[] { "m", "м" },
            ["g"]  = new[] { "g", "г" },
            ["kg"] = new[] { "kg", "кг" },
            ["ml"] = new[] { "ml", "мл" },
            ["l"]  = new[] { "l", "л" },
        };

        string? sourceUnitInName = null;
        foreach (var variants in unitVariants.Values)
        {
            foreach (var v in variants)
            {
                if (rawName.Contains(v))
                {
                    sourceUnitInName = v;
                    break;
                }
            }
            if (sourceUnitInName != null) break;
        }

        if (!string.IsNullOrEmpty(sourceUnitInName))
        {
            // Same unit family as target → good (1.0 or 0.75 for conversion)
            var targetFamily = GetUnitFamily(unit);
            var sourceFamily = GetUnitFamily(sourceUnitInName);

            if (targetFamily == sourceFamily && targetFamily != null)
            {
                // If the exact unit string matches, treat as direct
                if (unitVariants.TryGetValue(targetFamily, out var tVars) && tVars.Contains(unit) && tVars.Contains(sourceUnitInName))
                    return 1.0;

                return 0.75; // needs conversion (multiply/divide)
            }
        }

        return 0.4; // unit mismatch risk
    }

    private static string? GetUnitFamily(string u)
    {
        u = u.ToLowerInvariant();
        return u switch
        {
            "mm" or "мм" => "mm",
            "cm" or "см" => "cm",
            "m" or "м"   => "m",
            "g" or "г"   => "g",
            "kg" or "кг" => "kg",
            "ml" or "мл" => "ml",
            "l" or "л"   => "l",
            _ => null
        };
    }

    private static double ComputeDictionaryScore(
        CategoryAttribute attr,
        ExternalField field,
        OptionAlignmentHintDto? hint)
    {
        if (hint != null && hint.SourceAttributeId == attr.Id)
        {
            // Use precomputed quality
            var baseHint = Math.Min(100, hint.MatchScore) / 100.0;
            if (hint.ValueMapMappings.Count > 0)
                return Math.Max(0.65, baseHint * 0.9); // still good if map possible
            return baseHint;
        }

        // Fallback: try to compute overlap ourselves
        var localOpts = OptionAlignmentHelper.ParseAttributeOptions(attr.options);
        if (localOpts.Count == 0 || field.Options == null || field.Options.Count == 0) return 0.5;

        var mapEntries = OptionAlignmentHelper.BuildValueMapEntries(localOpts, field.Options);
        var coverage = localOpts.Count > 0 ? (double)mapEntries.Count / localOpts.Count : 0;

        if (coverage >= 0.8) return 0.95;
        if (coverage >= 0.5) return 0.75;
        return 0.45;
    }

    private static bool IsSemanticallySafe(ExternalField field, CompatibilityCandidate candidate, List<CategoryAttribute> attrs)
    {
        // Reject if would use truncate on critical import fields
        if (IsCriticalImportField(field.Key) && field.MaxLength.HasValue && field.MaxLength < 500)
        {
            // In compatibility mode we are conservative: if the candidate is free text -> risky
            if (candidate.SourceType == "attribute")
            {
                var attr = attrs.FirstOrDefault(a => a.Id == candidate.SourceAttributeId);
                if (attr != null && attr.type == AttributeType.Text) return false;
            }
        }
        return true;
    }

    private static bool IsCriticalImportField(string key)
    {
        var k = key.ToLowerInvariant();
        return k is "sku" or "article" or "barcode" or "price" or "quantity" or "stock";
    }

    private static bool WouldLoseDataOnImport(CategoryAttribute attr, ExternalField field)
    {
        if (field.MaxLength.HasValue && attr.type == AttributeType.Text)
            return true; // truncation risk
        return false;
    }

    /// <summary>Best standard productField match for an adapter field, if any.</summary>
    public static CompatibilityCandidate? TryMatchStandardProductField(ExternalField field)
        => TryProductFieldMatches(field).FirstOrDefault();

    private static List<CompatibilityCandidate> TryProductFieldMatches(ExternalField field)
    {
        var k = Normalize(field.Key);
        var n = Normalize(field.Name);
        var d = Normalize(field.Description ?? "");

        // Order matters: more specific tokens first.
        (string token, string field, double score)[] map =
        [
            ("offer_id", "sku", 0.93),
            ("offerid", "sku", 0.93),
            ("код продав", "sku", 0.92),
            ("артикул", "sku", 0.92),
            ("vendor", "sku", 0.88),
            ("sku", "sku", 0.92),
            ("article", "sku", 0.90),
            ("изображен", "images", 0.92),
            ("images", "images", 0.92),
            ("image", "images", 0.88),
            ("photo", "images", 0.85),
            ("аннотац", "description", 0.88),
            ("annotation", "description", 0.85),
            ("штрихкод", "barcode", 0.93),
            ("barcode", "barcode", 0.93),
            ("название", "name", 0.88),
            ("name", "name", 0.88),
            ("title", "name", 0.85),
            ("описание", "description", 0.80),
            ("description", "description", 0.80),
            ("цена", "price", 0.90),
            ("price", "price", 0.90),
            ("storagequantity", "storageQuantity", 0.82),
            ("остаток", "storageQuantity", 0.78),
            ("quantity", "storageQuantity", 0.78),
        ];

        foreach (var (token, productField, score) in map)
        {
            if (k.Contains(token) || n.Contains(token) || d.Contains(token))
            {
                return
                [
                    new CompatibilityCandidate
                    {
                        SourceType = "productField",
                        SourceField = productField,
                        Score = score,
                        Reason = "стандартное поле товара"
                    }
                ];
            }
        }
        return [];
    }

    private static bool IsObviousConstantField(ExternalField field)
    {
        var n = Normalize(field.Name + " " + (field.Description ?? ""));
        return n.Contains("страна") || n.Contains("country") ||
               n.Contains("бренд") || n.Contains("brand") ||
               n.Contains("ндс") || n.Contains("vat");
    }

    private static string? InferObviousConstant(ExternalField field)
    {
        var n = Normalize(field.Name);
        if (n.Contains("страна") || n.Contains("country")) return "Россия";
        if (n.Contains("бренд") || n.Contains("brand")) return null; // not obvious without store context
        if (n.Contains("ндс")) return "20";
        return null;
    }

    private static bool HasSemanticOverlap(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        var na = Normalize(a);
        var nb = Normalize(b);
        return na.Contains(nb) || nb.Contains(na) || PartialMatchScore(a, b) > 50;
    }

    private static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var t = s.Trim().ToLowerInvariant();
        t = Regex.Replace(t, @"[\s\-_]+", " ");
        return t;
    }

    /// <summary>
    /// True when there is exactly one viable candidate and its name clearly matches the adapter field.
    /// Asking the user "which attribute maps to X?" when X is the only option is pointless.
    /// </summary>
    private static bool IsObviousSingleMatch(
        string? adapterFieldName,
        string? adapterFieldKey,
        IReadOnlyList<CompatibilityCandidate> candidates)
    {
        var viable = candidates
            .Where(c => c.Score >= QuestionThreshold)
            .OrderByDescending(c => c.Score)
            .ToList();
        if (viable.Count != 1) return false;

        var best = viable[0];
        if (best.Score < 0.70) return false;
        if (best.SourceType is not ("attribute" or "productField")) return false;

        var sourceName = best.SourceAttributeName ?? best.SourceField ?? "";
        if (string.IsNullOrWhiteSpace(sourceName)) return false;

        var targets = new[] { adapterFieldName, adapterFieldKey }
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var target in targets)
        {
            if (ExactNormalizedMatch(sourceName, target)) return true;
            if (PartialMatchScore(sourceName, target) >= 72) return true;
        }

        return false;
    }

    /// <summary>Exposed for unit tests.</summary>
    public static bool TestIsObviousSingleMatch(
        string? adapterFieldName,
        string? adapterFieldKey,
        IReadOnlyList<CompatibilityCandidate> candidates)
        => IsObviousSingleMatch(adapterFieldName, adapterFieldKey, candidates);

    /// <summary>
    /// Generate 3-5 targeted clarifying questions from the report.
    /// Never more than maxQuestions.
    /// </summary>
    private static List<CompatibilityQuestion> GenerateQuestions(
        CompatibilityReport report,
        List<CategoryAttribute> localAttributes,
        IReadOnlyList<ExternalField> adapterFields,
        SchemaContextDto schemaContext,
        int maxQuestions)
    {
        var questions = new List<CompatibilityQuestion>();

        // 1. Source pick for ambiguous (skip when the answer is already obvious)
        foreach (var amb in report.AmbiguousPairs.Take(2))
        {
            if (questions.Count >= maxQuestions) break;
            if (IsObviousSingleMatch(amb.AdapterFieldName, amb.AdapterFieldKey, amb.Candidates))
                continue;

            var cands = amb.Candidates.Select(c => new QuestionCandidate
            {
                SourceType = c.SourceType,
                SourceAttributeId = c.SourceAttributeId,
                SourceAttributeName = c.SourceAttributeName,
                SourceField = c.SourceField,
                Score = c.Score
            }).ToList();

            questions.Add(new CompatibilityQuestion
            {
                Id = $"q_src_{amb.AdapterFieldKey}",
                Type = "source_pick",
                AdapterFieldKey = amb.AdapterFieldKey,
                AdapterFieldName = amb.AdapterFieldName,
                Candidates = cands,
                Question = $"Какой атрибут вашей категории соответствует полю «{amb.AdapterFieldName}» на маркетплейсе?",
                DefaultRecommendation = cands.FirstOrDefault()?.SourceAttributeName,
                Impact = amb.IsRequired ? "export_and_import" : "export"
            });
        }

        // 2. Dictionary confirm for cross-marketplace risks or select ambiguous
        foreach (var risk in report.CrossMarketplaceRisks.Take(1))
        {
            if (questions.Count >= maxQuestions) break;
            var attr = localAttributes.FirstOrDefault(a => a.Id == risk.SourceAttributeId);
            if (attr == null) continue;

            questions.Add(new CompatibilityQuestion
            {
                Id = $"q_dict_{risk.AdapterFieldKey}",
                Type = "dictionary_confirm",
                AdapterFieldKey = risk.AdapterFieldKey,
                Candidates = new List<QuestionCandidate>
                {
                    new() { SourceType = "attribute", SourceAttributeId = risk.SourceAttributeId, SourceAttributeName = attr.name, Score = risk.MatchScore / 100.0 },
                    new() { SourceType = "manual", SourceAttributeName = "Да, использовать optionMap", Score = 0.95 },
                    new() { SourceType = "manual", SourceAttributeName = "Нет, маппинг не нужен", Score = 0.6 },
                    new() { SourceType = "manual", SourceAttributeName = "Указать вручную", Score = 0.5 }
                },
                Question = $"Словарь для «{risk.AdapterFieldKey}» импортирован из другого маркетплейса. Как поступить?",
                DefaultRecommendation = "Да, использовать optionMap",
                Impact = "export_and_import"
            });
        }

        // 3. Unit confirm
        var unitGaps = adapterFields
            .Where(f => !string.IsNullOrEmpty(f.Unit) && report.AmbiguousPairs.Any(a => a.AdapterFieldKey == f.Key) || report.RequiredGaps.Any(g => g.AdapterFieldKey == f.Key))
            .Take(1);
        foreach (var f in unitGaps)
        {
            if (questions.Count >= maxQuestions) break;
            questions.Add(new CompatibilityQuestion
            {
                Id = $"q_unit_{f.Key}",
                Type = "unit_confirm",
                AdapterFieldKey = f.Key,
                AdapterFieldName = f.Name,
                Candidates = new List<QuestionCandidate>
                {
                    new() { SourceType = "manual", SourceAttributeName = "Да, применить множитель", Score = 0.9 },
                    new() { SourceType = "manual", SourceAttributeName = "Нет, единицы уже совпадают", Score = 0.7 },
                    new() { SourceType = "manual", SourceAttributeName = "Указать вручную", Score = 0.5 }
                },
                Question = $"Поле «{f.Name}» ожидает единицы «{f.Unit}». Как поступить?",
                DefaultRecommendation = "Да, применить multiply",
                Impact = "export"
            });
        }

        // 4. Semantic confirm (e.g. model vs name)
        foreach (var gap in report.RequiredGaps.Take(1))
        {
            if (questions.Count >= maxQuestions) break;
            if (gap.AdapterFieldKey.Contains("model", StringComparison.OrdinalIgnoreCase) ||
                gap.AdapterFieldKey.Contains("модель", StringComparison.OrdinalIgnoreCase))
            {
                questions.Add(new CompatibilityQuestion
                {
                    Id = $"q_sem_{gap.AdapterFieldKey}",
                    Type = "semantic_confirm",
                    AdapterFieldKey = gap.AdapterFieldKey,
                    Candidates = new List<QuestionCandidate>
                    {
                        new() { SourceType = "manual", SourceAttributeName = "Использовать из названия товара", Score = 0.85 },
                        new() { SourceType = "manual", SourceAttributeName = "Использовать из атрибута «Модель»", Score = 0.75 },
                        new() { SourceType = "manual", SourceAttributeName = "Оставить пустым / указать вручную", Score = 0.5 }
                    },
                    Question = $"Поле «{gap.AdapterFieldName ?? gap.AdapterFieldKey}» — уточните смысл.",
                    DefaultRecommendation = "Использовать из названия",
                    Impact = "export_and_import"
                });
            }
        }

        // 5. Skip or constant for obvious adapter-only required gaps
        foreach (var ao in report.AdapterOnly.Where(a => a.IsRequired).Take(1))
        {
            if (questions.Count >= maxQuestions) break;
            questions.Add(new CompatibilityQuestion
            {
                Id = $"q_skip_{ao.Key}",
                Type = "skip_field",
                AdapterFieldKey = ao.Key,
                AdapterFieldName = ao.Name,
                Candidates = new List<QuestionCandidate>
                {
                    new() { SourceType = "manual", SourceAttributeName = "Оставить пустым", Score = 0.9 },
                    new() { SourceType = "manual", SourceAttributeName = "Задать константу", Score = 0.7 },
                    new() { SourceType = "manual", SourceAttributeName = "Указать источник вручную", Score = 0.6 }
                },
                Question = $"Обязательное поле «{ao.Name}» не имеет явного источника. Что делать?",
                DefaultRecommendation = "Оставить пустым",
                Impact = "export"
            });
        }

        return questions.Take(maxQuestions).ToList();
    }

    // --- Test hooks for unit tests (PR1) ---
    /// <summary>Exposed for unit testing scoring logic only.</summary>
    public static double TestComputeNameScore(CategoryAttribute attr, ExternalField field)
        => ComputeNameScore(attr, field);

    /// <summary>Exposed for unit testing scoring logic only.</summary>
    public static double TestComputeTypeScore(AttributeType localType, string adapterType)
        => ComputeTypeScore(localType, adapterType);

    /// <summary>Exposed for unit testing scoring logic only.</summary>
    public static double TestComputeUnitScore(CategoryAttribute attr, ExternalField field)
        => ComputeUnitScore(attr, field);

    // --- Public DTOs for the service ---

    public class CompatibilityAnalysisResult
    {
        public int MappingId { get; set; }
        public DateTime AnalyzedAt { get; set; }
        public CompatibilityReport Report { get; set; } = new();
        public List<CompatibilityQuestion> Questions { get; set; } = [];
        public string? SchemaContextHint { get; set; }
    }

    public class CompatibilityReport
    {
        public int MappingId { get; set; }
        public int LocalCategoryId { get; set; }
        public string? AdapterKey { get; set; }
        public CrossMarketplaceInfo? SchemaSource { get; set; }

        public List<ClearMatch> ClearMatches { get; set; } = [];
        public List<AmbiguousPair> AmbiguousPairs { get; set; } = [];
        public List<IncompatiblePair> Incompatible { get; set; } = [];
        public List<AdapterOnlyField> AdapterOnly { get; set; } = [];
        public List<LocalOnlyAttribute> LocalOnly { get; set; } = [];
        public List<CrossMarketplaceRisk> CrossMarketplaceRisks { get; set; } = [];
        public List<RequiredGap> RequiredGaps { get; set; } = [];

        public double CompatibilityScore { get; set; }
        public ReportSummary Summary { get; set; } = new();
    }

    public class CrossMarketplaceInfo
    {
        public string? FromConnectionName { get; set; }
        public string? FromAdapterKey { get; set; }
        public string? Hint { get; set; }
    }

    public class ClearMatch
    {
        public string AdapterFieldKey { get; set; } = "";
        public string AdapterFieldName { get; set; } = "";
        public bool IsRequired { get; set; }
        public bool IsAspect { get; set; }
        public CompatibilityCandidate BestSource { get; set; } = null!;
        public double Score { get; set; }
    }

    public class AmbiguousPair
    {
        public string AdapterFieldKey { get; set; } = "";
        public string AdapterFieldName { get; set; } = "";
        public bool IsRequired { get; set; }
        public List<CompatibilityCandidate> Candidates { get; set; } = [];
        public string? Reason { get; set; }
    }

    public class IncompatiblePair
    {
        public string AdapterFieldKey { get; set; } = "";
        public string AdapterName { get; set; } = "";
        public CompatibilityCandidate Candidate { get; set; } = null!;
        public string Reason { get; set; } = "";
    }

    public class AdapterOnlyField
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool IsRequired { get; set; }
        public string? Unit { get; set; }
        public string Reason { get; set; } = "";
    }

    public class LocalOnlyAttribute
    {
        public int AttributeId { get; set; }
        public string Name { get; set; } = "";
        public string Key { get; set; } = "";
        public string Type { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    public class CrossMarketplaceRisk
    {
        public string AdapterFieldKey { get; set; } = "";
        public int SourceAttributeId { get; set; }
        public int MatchScore { get; set; }
        public int UnmappedLocalCount { get; set; }
        public string? Note { get; set; }
    }

    public class RequiredGap
    {
        public string AdapterFieldKey { get; set; } = "";
        public string AdapterFieldName { get; set; } = "";
        public CompatibilityCandidate? TopCandidate { get; set; }
        public double Score { get; set; }
        public string Reason { get; set; } = "";
    }

    public class ReportSummary
    {
        public int TotalAdapterFields { get; set; }
        public int ClearMatches { get; set; }
        public int Ambiguous { get; set; }
        public int IntentionallyLeftEmpty { get; set; }
        public int RequiredGaps { get; set; }
        public int RequiredCoveredHighConfidence { get; set; }
    }

    public class CompatibilityCandidate
    {
        public string SourceType { get; set; } = ""; // attribute | productField | static
        public int? SourceAttributeId { get; set; }
        public string? SourceAttributeName { get; set; }
        public string? SourceField { get; set; }
        public string? StaticValue { get; set; }
        public double Score { get; set; }
        public string? Reason { get; set; }
    }

    public class CompatibilityQuestion
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = ""; // source_pick, dictionary_confirm, unit_confirm, semantic_confirm, skip_field, constant_value
        public string? AdapterFieldKey { get; set; }
        public string? AdapterFieldName { get; set; }
        public List<QuestionCandidate> Candidates { get; set; } = [];
        public string Question { get; set; } = "";
        public string? DefaultRecommendation { get; set; }
        public string? Impact { get; set; }
    }

    public class QuestionCandidate
    {
        public string SourceType { get; set; } = "";
        public int? SourceAttributeId { get; set; }
        public string? SourceAttributeName { get; set; }
        public string? SourceField { get; set; }
        public double Score { get; set; }
    }
}
