/** Accepted/terminal plan state is monotonic within a conversation. */
export function reconcilePlanMessages(current, incoming) {
  const byProposal = new Map(
    current
      .filter((m) => m.plan?.proposalId)
      .map((m) => [m.plan.proposalId, m]),
  );
  const next = incoming.map((message) => {
    const local = byProposal.get(message.plan?.proposalId);
    if (!local) return message;
    const localAdvanced =
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
  return next;
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
    !message.planConfirmationError
  );
}
