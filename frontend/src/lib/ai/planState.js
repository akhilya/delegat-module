import { finalizeTurnTools } from "./persist.js";

function isTerminalState(m) {
  return !!(m?.done || (m?.messageState && m.messageState !== "running"));
}

/**
 * Merge a local canonical row with its server counterpart by stable
 * messageId. Same run: the higher lastEventSequence projection wins (equal
 * sequence defers to a terminal server snapshot). Different runs: counters
 * are never compared across runs - an execution phase is always newer than a
 * proposal/chat phase of the same row. The merged row carries the real
 * incoming dbId and the stable local UI id; permitted optimistic user
 * decisions survive while a merely-local stopped flag never beats an
 * authoritative terminal snapshot.
 */
function mergeCanonicalTurn(local, incoming) {
  // Optimistic user decisions survive only while they describe the same still
  // unprocessed proposal; a confirmed/executed/terminal winner is server
  // authority and drops the stale local flags.
  const carry = (winner) => {
    const sameProposal =
      local.plan?.proposalId &&
      local.plan.proposalId === winner.plan?.proposalId;
    const terminal = isTerminalState(winner);
    // A pending user decision stays monotonic only while the same proposal
    // is still actionable server-side; a processed or dead turn owns the
    // flags.
    const keepDecisions =
      sameProposal &&
      !winner.planConfirmed &&
      !winner.planExecuted &&
      !winner.planFailed &&
      !winner.turnStopped &&
      winner.messageState !== "interrupted" &&
      winner.messageState !== "stopped";
    return {
      ...winner,
      id: local.id ?? incoming.id,
      dbId: incoming.dbId ?? local.dbId,
      planCancelled: keepDecisions
        ? incoming.planCancelled || local.planCancelled || undefined
        : winner.planCancelled,
      planSuperseded: keepDecisions
        ? incoming.planSuperseded || local.planSuperseded || undefined
        : winner.planSuperseded,
      planConfirming: terminal ? false : local.planConfirming,
      planConfirmationError: winner.planExecuted
        ? null
        : local.planConfirmationError,
    };
  };
  const sameRun = !!(local.runId && local.runId === incoming.runId);
  const orphaned =
    sameRun &&
    incoming.messageState === "interrupted" &&
    incoming.requestError?.code === "ai_turn_interrupted";
  if (orphaned) {
    // A process-loss read projection is authoritative over a still-running
    // local: keep the richer local projection (partial text, tools, plan)
    // but end the turn honestly - never infer completion or a user stop.
    const richer =
      (local.lastEventSequence ?? -1) > (incoming.lastEventSequence ?? -1)
        ? local
        : incoming;
    const merged = {
      ...carry(richer),
      done: true,
      messageState: "interrupted",
      requestError: incoming.requestError,
      planConfirming: false,
    };
    if (merged.phase === "execution" && !merged.executionOutcome) {
      merged.executionOutcome = {
        status: "interrupted",
        outcomeUnknown: true,
      };
    }
    merged.toolCalls = merged.toolCalls?.map((tc) => ({ ...tc }));
    finalizeTurnTools(merged);
    return merged;
  }
  if (sameRun) {
    const localSeq = Number.isSafeInteger(local.lastEventSequence)
      ? local.lastEventSequence
      : -1;
    const incomingSeq = Number.isSafeInteger(incoming.lastEventSequence)
      ? incoming.lastEventSequence
      : -1;
    if (localSeq !== incomingSeq)
      return carry(localSeq > incomingSeq ? local : incoming);
    return carry(
      isTerminalState(local) && !isTerminalState(incoming) ? local : incoming,
    );
  }
  const localExec = local.phase === "execution";
  const incomingExec = incoming.phase === "execution";
  if (localExec !== incomingExec) return carry(localExec ? local : incoming);
  return carry(
    isTerminalState(incoming) && !isTerminalState(local) ? incoming : local,
  );
}

/** Accepted/terminal plan state is monotonic within a conversation. */
export function reconcilePlanMessages(current, incoming) {
  const stopped = current.filter((m) => m.turnStopped);
  incoming = incoming.map((message) => {
    // Canonical rows reconcile by identity below, where a newer server
    // terminal snapshot correctly beats a merely local stopped flag.
    if (message?.messageId) return message;
    const local = stopped.find((m) => m.dbId && m.dbId === message.dbId);
    return local && !message.turnStopped ? { ...local } : message;
  });

  // Canonical rows reconcile by stable messageId BEFORE any proposal/parent
  // heuristic: a begin-time server row (seq 0, running) must never replace
  // the finished local turn, and locals omitted by a stale read stay.
  const canonicalLocals = new Map(
    current.filter((m) => m?.messageId).map((m) => [m.messageId, m]),
  );
  const reconciledCanonical = new Set();
  incoming = incoming.map((message) => {
    const local = message?.messageId && canonicalLocals.get(message.messageId);
    if (!local) return message;
    const merged = mergeCanonicalTurn(local, message);
    reconciledCanonical.add(merged);
    return merged;
  });
  // Preserve canonical locals the stale read omitted, in existing chronology:
  // each one lands right after the last current row still present, or at its
  // dbId position when it has one.
  let anchor = -1;
  for (const local of current) {
    let index = -1;
    if (local?.messageId) {
      index = incoming.findIndex((m) => m.messageId === local.messageId);
    }
    if (index < 0 && local?.dbId != null) {
      index = incoming.findIndex((m) => m.dbId === local.dbId);
    }
    if (index >= 0) {
      anchor = index;
      continue;
    }
    if (!local?.messageId) continue;
    const byDb =
      local.dbId != null
        ? incoming.findIndex((m) => m.dbId != null && m.dbId > local.dbId)
        : -1;
    const at = byDb >= 0 ? byDb : anchor + 1;
    incoming.splice(at, 0, local);
    reconciledCanonical.add(local);
    anchor = at;
  }
  const last = current.at(-1);
  if (last?.turnStopped && !last.dbId && !last.plan) {
    const index = current.length - 1;
    if (incoming.length === index) incoming = [...incoming, last];
    else if (
      incoming.length === current.length &&
      incoming[index]?.role === "assistant" &&
      !incoming[index].turnStopped
    )
      incoming[index] = { ...last, dbId: incoming[index].dbId };
  }
  const byProposal = new Map(
    current
      .filter((m) => m.plan?.proposalId)
      .map((m) => [m.plan.proposalId, m]),
  );
  const next = incoming.map((message) => {
    // Already settled by canonical identity: no anonymous proposal merge.
    if (reconciledCanonical.has(message)) return message;
    const local = byProposal.get(message.plan?.proposalId);
    // Unequal canonical identities are different turns: they never merge
    // solely because parent/own proposal fields happen to match.
    if (
      local?.messageId &&
      message.messageId &&
      local.messageId !== message.messageId
    )
      return message;
    if (!local) return message;
    const localAdvanced =
      (local.turnStopped && !message.turnStopped) ||
      (local.planConfirmed && !message.planConfirmed) ||
      (local.planExecuted && !message.planExecuted) ||
      (local.planConfirmed &&
        Number.isInteger(local.planExecStart) &&
        !Number.isInteger(message.planExecStart));
    if (localAdvanced) return { ...local, dbId: message.dbId ?? local.dbId };
    return {
      ...message,
      id: local.id,
      planCancelled: message.planCancelled || local.planCancelled,
      planSuperseded: message.planSuperseded || local.planSuperseded,
      planConfirming: local.planConfirming,
      planConfirmationError: message.planExecuted
        ? null
        : local.planConfirmationError,
    };
  });
  // A read that raced persistence may not contain the accepted proposal yet.
  for (const local of byProposal.values()) {
    if (
      (local.planConfirmed || local.planConfirmationError) &&
      !next.some((m) => m.plan?.proposalId === local.plan.proposalId)
    ) {
      const later = local.dbId
        ? next.findIndex((m) => m.dbId > local.dbId)
        : -1;
      next.splice(later < 0 ? next.length : later, 0, local);
    }
  }
  // The parent link scopes reviews but is not a unique identity: several
  // reviews can share one executed plan. Keep locals that no incoming row
  // proves to be the same review.
  for (const local of current.filter((m) => m.executionProposalId)) {
    if (next.some((candidate) => sameReviewIdentity(local, candidate, current, next)))
      continue;
    const later = local.dbId ? next.findIndex((m) => m.dbId > local.dbId) : -1;
    next.splice(later < 0 ? next.length : later, 0, local);
  }
  return next;
}

function sameReviewIdentity(local, candidate, current, next) {
  const parent = local.executionProposalId;
  if (
    typeof parent !== "string" ||
    !parent ||
    candidate.executionProposalId !== parent
  )
    return false;
  // Canonical identity is authoritative: a stable messageId match is the same
  // turn, and an unequal or missing counterpart is a different one.
  if (local.messageId || candidate.messageId)
    return local.messageId === candidate.messageId;
  if (local.dbId && candidate.dbId) return local.dbId === candidate.dbId;
  if (local.id && candidate.id && local.id === candidate.id) return true;
  const localPlan = local.plan?.proposalId;
  const candidatePlan = candidate.plan?.proposalId;
  if (localPlan || candidatePlan)
    return !!localPlan && localPlan === candidatePlan;
  // Anonymous reviews only collapse onto a sole unambiguous counterpart.
  const anonymous = (list) =>
    list.filter(
      (m) => m.executionProposalId === parent && !m.plan?.proposalId,
    ).length;
  return anonymous(current) === 1 && anonymous(next) === 1;
}

export function canConfirmPlan(message) {
  return !!(
    message?.plan &&
    message.done &&
    // A canonical row interrupted by process loss cannot be confirmed: the
    // proposal may never have reached the server.
    message.messageState !== "interrupted" &&
    !message.planConfirmed &&
    !message.planCancelled &&
    !message.planSuperseded &&
    !message.planExecuted &&
    !message.planFailed &&
    !message.planConfirming &&
    !message.planConfirmationError &&
    !message.turnStopped
  );
}
