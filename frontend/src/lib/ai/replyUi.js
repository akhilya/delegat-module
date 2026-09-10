/**
 * Reply chrome from navigate_to / ask_form. Secrets never belong on a button
 * or in a follow-up template — the host already strips them; this is the SPA
 * safety net for stored threads.
 */

const FIELD_KEY = /^[a-zA-Z][a-zA-Z0-9_]{0,39}$/;
const ADAPTER_KEY = /^[a-zA-Z0-9._-]{1,64}$/;
const SAFE_PATH = /^\/[a-zA-Z0-9\-/._]*(\?[A-Za-z0-9._~%=&+,-]*)?$/;

function safeInAppPath(raw) {
  if (typeof raw !== "string") return "";
  const url = raw.trim();
  if (!url.startsWith("/") || url.length > 180 || !SAFE_PATH.test(url)) return "";
  return url;
}

export function normalizeChatAction(raw) {
  const url = safeInAppPath(raw?.url);
  const prompt =
    !url && typeof raw?.prompt === "string" ? raw.prompt.trim().slice(0, 200) : "";
  const label = String(raw?.label || url || prompt || "")
    .trim()
    .slice(0, 80);
  if (!label || (!url && !prompt)) return null;
  return {
    url,
    prompt,
    label,
    kind: url ? "open" : "continue",
  };
}

export function chatActionsFromTool(tool) {
  const result = tool?.result;
  if (!result || typeof result !== "object") return [];
  const raw = Array.isArray(result.actions) ? result.actions : null;
  const list = raw?.length
    ? raw
    : result.navigate && (result.url || result.prompt)
      ? [
          {
            url: result.url || "",
            label: result.label || result.url || result.prompt,
            prompt: result.prompt || "",
          },
        ]
      : [];
  return list.map(normalizeChatAction).filter(Boolean).slice(0, 6);
}

function formatAnswerValue(value) {
  if (Array.isArray(value)) {
    return value.map((item) => String(item ?? "").trim()).filter(Boolean).join(", ");
  }
  return value == null ? "" : String(value);
}

export function interpolateFollowUp(template, values) {
  const normalized = {};
  for (const [key, value] of Object.entries(values || {})) {
    const text = formatAnswerValue(value);
    if (text) normalized[key] = text;
  }
  const text = String(template || "");
  if (!text) {
    return Object.entries(normalized)
      .map(([key, value]) => `${key}: ${value}`)
      .join("\n");
  }
  return text.replace(/\{([a-zA-Z][a-zA-Z0-9_]{0,39})\}/g, (_, key) => {
    return normalized[key] ?? "";
  });
}

/** Labels for a submitted questionnaire — shown on the card, not as a chat bubble. */
export function recapFormAnswers(questions, answers) {
  const rows = [];
  for (const question of questions || []) {
    const raw = answers?.[question.key];
    if (raw == null || raw === "") continue;
    const values = Array.isArray(raw) ? raw : [raw];
    const labels = values
      .map((value) => {
        const text = formatAnswerValue(value);
        if (!text) return "";
        const choice = (question.choices || []).find((item) => item.value === text);
        return choice?.label || text;
      })
      .filter(Boolean);
    if (!labels.length) continue;
    rows.push({
      key: question.key,
      title: question.title || question.key,
      answer: labels.join(", "),
    });
  }
  return rows;
}

function clip(value, max) {
  return String(value || "").trim().slice(0, max);
}

function questionType(raw) {
  const type = String(raw || "").toLowerCase();
  if (type === "password" || type === "secret") return "secret";
  if (type === "multiple" || type === "multi" || type === "checkbox") return "multiple";
  if (type === "text" || type === "freeform" || type === "input" || type === "textarea") {
    return "text";
  }
  if (type === "select" || type === "choice" || type === "single" || type === "radio") {
    return "single";
  }
  return "";
}

function normalizeChoices(raw) {
  const choices = [];
  for (const option of Array.isArray(raw) ? raw : []) {
    if (choices.length >= 8) break;
    const value = String(option?.value ?? "").trim().slice(0, 80);
    if (!value) continue;
    choices.push({
      value,
      label: clip(option?.label || option?.value, 80) || value,
      description: clip(option?.description, 120),
    });
  }
  return choices;
}

export function normalizeQuestion(raw) {
  const key = String(raw?.key || raw?.name || "").trim();
  if (!FIELD_KEY.test(key)) return null;
  if (raw?.secret === true || raw?.isSecret === true) return null;
  const typeHint = questionType(raw?.type);
  if (typeHint === "secret") return null;
  const choices = normalizeChoices(raw?.choices || raw?.options);
  const type = typeHint || (choices.length ? "single" : "text");
  const item = {
    key,
    title: clip(raw?.title || raw?.label || key, 80),
    description: clip(raw?.description, 160),
    type,
    required: raw?.required !== false,
    placeholder: clip(raw?.placeholder, 80),
    allowOther: raw?.allowOther === true,
    choices: type === "text" ? [] : choices,
  };
  if (item.type === "text") item.allowOther = false;
  if (item.type !== "text" && !item.choices.length && !item.allowOther) return null;
  return item;
}

export function normalizeAskForm(result) {
  if (!result || result.form !== true) return null;
  const kindRaw = String(result.kind || "").toLowerCase();
  const title = clip(result.title, 80);
  const submitLabel = clip(result.submitLabel, 40);
  if (kindRaw === "connection" || kindRaw === "connect") {
    const adapterKey = String(result.adapterKey || "").trim();
    if (!ADAPTER_KEY.test(adapterKey)) return null;
    return {
      kind: "connection",
      adapterKey,
      name: clip(result.name, 80),
      title,
      submitLabel,
    };
  }
  if (kindRaw !== "questions" && kindRaw !== "question" && kindRaw !== "fields") {
    return null;
  }
  const rawList = Array.isArray(result.questions) && result.questions.length
    ? result.questions
    : Array.isArray(result.fields)
      ? result.fields
      : [];
  const questions = [];
  for (const raw of rawList) {
    if (questions.length >= 5) break;
    const question = normalizeQuestion(raw);
    if (question) questions.push(question);
  }
  if (!questions.length) return null;
  return {
    kind: "questions",
    title,
    submitLabel,
    followUp: clip(result.followUp, 400),
    questions,
  };
}

export function askFormFromTool(tool) {
  return normalizeAskForm(tool?.result);
}

export function answersFromFormData(questions, formData, skipped = {}) {
  const values = {};
  for (const question of questions || []) {
    if (skipped[question.key] === "skipped") continue;
    if (question.type === "multiple") {
      const all = formData.getAll(question.key).map((item) => String(item).trim()).filter(Boolean);
      if (all.length) values[question.key] = all;
      continue;
    }
    const value = String(formData.get(question.key) ?? "").trim();
    if (value) values[question.key] = value;
  }
  return values;
}
