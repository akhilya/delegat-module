/** Accepted/terminal plan state is monotonic within a conversation. */
export function reconcilePlanMessages(current, incoming) {
  const stopped = current.filter((m) => m.turnStopped);
  incoming = incoming.map((message) => {
    const local = stopped.find((m) => m.dbId && m.dbId === message.dbId);
    return local && !message.turnStopped ? { ...local } : message;
  });
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
    const local = byProposal.get(message.plan?.proposalId);
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
