# Fair fixed-route fleet baseline: one shared, sensibly-ordered loop, staggered

**Context.** The thesis claim rests on Dynamic beating a fixed-route baseline under
identical demand. With multiple buses, a badly-chosen baseline would invite the
"strawman" critique — either too weak (a silly loop order) or unfairly strong (routes
pre-partitioned using demand knowledge the baseline shouldn't have).

**Decision.** The fixed-route fleet is **N buses running the *same* full loop of all stops**,
**phase-offset** by their distributed start positions (a headway model — N buses give N×
better headway). The loop order is a **nearest-neighbor tour** computed once at startup and
shared by all baseline buses, replacing the previous arbitrary `StopId` order.

**Why.**
- *Same loop, staggered* is the honest real-world status quo (several shuttles on one line)
  and introduces no demand knowledge, so it isolates exactly what Dynamic's demand-response buys.
- *Nearest-neighbor tour* removes the "your baseline drove a needlessly long route" objection:
  Dynamic must beat a *competently* routed fixed service, not a zig-zag. `StopId` order was
  arbitrary and could be pathologically long.
- Route *partitioning* (disjoint sub-loops per bus) was rejected: it is not the status quo and
  it bakes demand assumptions into the baseline, muddying the comparison.

**Consequence.** Both modes use identical distributed start positions (ADR-adjacent, from the
fleet spec) so neither is advantaged. The nearest-neighbor tour is a fixed input, not
re-optimized per run, keeping the baseline deterministic.
