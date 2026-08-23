# Centralized fleet assignment (not a decentralized auction)

**Context.** Going multi-bus requires deciding *which* bus serves each new request. The
thesis is titled "Agent-Based" transit, which superficially suggests decentralized,
autonomous vehicles negotiating (e.g. a contract-net auction where buses bid their
insertion cost).

**Decision.** A single centralized **Fleet Optimizer** agent assigns each waiting request
to the bus with the cheapest feasible insertion (reusing the existing `InsertionPlanner`
cost as the per-bus oracle) and commits it. Buses do not negotiate.

**Why.** It is the direct generalization of the existing single-bus code, is deterministic
and near-optimal at the 2–4 bus scale of this thesis, and mirrors how real
demand-responsive transit is actually controlled (a central control room). A bidding
auction whose bid equals the insertion cost would be *behaviorally identical* here while
adding coordination machinery (bid collection, tie-breaks, double-assignment prevention)
for no measurable gain. The "agent-based" character is carried by the demand, dispatch,
and monitoring agents, not by making assignment decentralized.

**Consequence.** The agent structure stays a shared blackboard with one optimizer, not a
message-passing negotiation. If future work needs many vehicles or fault tolerance, this
is the decision to revisit — the insertion-cost oracle would become each agent's bid function.
