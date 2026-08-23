# Campus Transit Simulation

The Unity-side domain: simulated demand-responsive bus logistics (demand, routing,
scheduling, metrics) measured against a fixed-route baseline. The physical `rc_transit`
sub-project is a *separate* domain (its vehicle is a "car", not a "bus").

## Language

**Fleet**:
The set of buses operating under one dispatcher in a single run.

**Bus**:
A capacity-limited vehicle that picks up and drops off passenger requests along a plan.
_Avoid_: vehicle, car (those name the physical `rc_transit` robot, a different domain).

**Request**:
A single passenger trip from an origin stop to a destination stop.
_Avoid_: passenger, trip, job, ride.

**Stop**:
A road-graph node bound to a building where passengers wait and are served.
_Avoid_: station, node (a node is the graph primitive; a stop is a node that is a served location).

**Plan**:
A bus's ordered list of pickup/dropoff/visit tasks it will execute next.
_Avoid_: route, schedule, itinerary.

**Assignment**:
Binding a request to the one bus that will serve it. In this system assignment is
**centralized** (see ADR 0001).

**Fleet Optimizer**:
The centralized agent that assigns each waiting request to a bus (cheapest feasible
insertion) and updates that bus's plan.
_Avoid_: dispatcher (ambiguous with **Dispatch**, which *executes* not assigns).

**Dispatch**:
The per-bus executor that drives a bus along its plan — leg travel plus board/alight.
It does **not** decide which bus serves a request; that is the Fleet Optimizer's job.
_Avoid_: using "dispatch" to mean assignment.
