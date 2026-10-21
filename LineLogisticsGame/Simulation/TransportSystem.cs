using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using LineLogisticsGame.Geometry;
using LineLogisticsGame.World;
using Point = LineLogisticsGame.Geometry.Point;

namespace LineLogisticsGame.Simulation
{
    // Output lines ("green lines") produce a resource of their type every SpawnInterval seconds; consumer lines ("red lines")
    // take resources of any type and consume one every ConsumeInterval seconds.
    // A resource that Recipes says requires others is produced by a group of lines built together (same Group):
    // an input line for every required type, which takes deliveries like a red line, and the output line. The output line only
    // produces when every input line has one of its resources to give, and consumes one from each.
    // Transporters move them, driving only on roads (in the direction they were drawn) and loading and unloading at the lines' ends.
    public class TransportSystem
    {
        public ResourceRecipes Recipes = new ResourceRecipes();
        public Color ResourceColor = Color.Gold;
        public Color TransporterColor = Color.DeepSkyBlue;

        public float SpawnInterval = 5f;
        public float ConsumeInterval = 5f;
        public float ResourceRadius = 5f;
        public float ResourceSpacing = 9f;      // center to center, on green and red lines and between wagons; a bit less than the diameter so they overlap slightly
        public float ConveyorSpeed = 40f;       // how fast resources slide along green lines
        public float TransporterRadius = 8f;
        public float TransporterGap = 2f;       // how close transporters (and their wagons) may get to each other
        public float StoppedPenalty = 500f;     // extra route cost of a line with a transporter standing on it
        public float MaxStillTime = 30f;        // seconds a transporter may stand still before it is removed (with whatever it carries)
        public bool Paused = false;
        public long Points;  // earned for every resource a consumer line consumes (see ResourceRecipes.PointsOf)
        public Action<Vector2, long> PointsEarned;  // where a resource was consumed and the points it earned
        public Statistics Statistics = new Statistics();    // what was produced, consumed and earned over time

        public List<Resource> Resources = new List<Resource>();
        public List<Transporter> Transporters = new List<Transporter>();
        public List<Depot> Depots = new List<Depot>();
        public float DepotRadius = 11f;
        public Dictionary<StyledSegment, SourceLine> SourceLines = new Dictionary<StyledSegment, SourceLine>();
        public Dictionary<StyledSegment, SinkLine> SinkLines = new Dictionary<StyledSegment, SinkLine>();

        private readonly Dictionary<StyledSegment, float> timers = new Dictionary<StyledSegment, float>();
        private readonly Random random = new Random();
        private Dictionary<Transporter, Occupancy> occupied = new Dictionary<Transporter, Occupancy>();
        private LineDrawing drawing;
        private HashSet<StyledSegment> alive = new HashSet<StyledSegment>();    // drawing.Segments, for fast lookups

        // Resources grouped by the line they lie on (see OnLine). Kept up to date as resources are added and removed here;
        // rebuilt when stale: at the start of every update and after resources moved to other lines.
        private readonly Dictionary<StyledSegment, List<Resource>> resourceIndex = new Dictionary<StyledSegment, List<Resource>>();
        private bool resourceIndexStale = true;
        private static readonly List<Resource> noResources = new List<Resource>();

        // walkable roads leaving each point (from A to B), in drawing order, and input lines by group; rebuilt every update (see IndexLines)
        private Dictionary<Point, List<StyledSegment>> roadsFrom = new Dictionary<Point, List<StyledSegment>>();
        private Dictionary<int, List<StyledSegment>> inputLines = new Dictionary<int, List<StyledSegment>>();    // by Group
        private static readonly List<StyledSegment> noRoads = new List<StyledSegment>();

        // lines transporters pick up from, and the type of resource they hold
        public bool IsSource(StyledSegment segment) => segment.Kind == LineKind.Output;
        public int SourceType(StyledSegment segment) => segment.Type;

        // lines transporters deliver to, and whether they take a type of resource: input lines take their type, consumer lines any
        public bool IsSink(StyledSegment segment) => segment.Kind == LineKind.Input || segment.Kind == LineKind.Consumer;
        public bool Accepts(StyledSegment segment, int type) => segment.Kind == LineKind.Consumer || segment.Type == type;

        public bool IsBidirectional(StyledSegment segment) => IsSource(segment) || IsSink(segment);

        // Only roads are driven on, in the direction they were drawn. Transporters load and unload standing on the road
        // at a line's end; one that ended up on a line anyway (the line was drawn over it) can still drive off it either way.
        public bool IsWalkable(StyledSegment segment) => segment.Kind == LineKind.Road;

        public Color ResourceColorOf(int type) => ResourceRecipes.ColorOf(type);

        public Transporter AddTransporter(StyledSegment segment, Vector2 near, int capacity, float speed)
        {
            var transporter = new Transporter
            {
                Segment = segment,
                Distance = ProjectDistance(segment, near),
                Capacity = capacity,
                radius = TransporterRadius,
                color = TransporterColor,
                speed = speed
            };
            transporter.position = PositionOn(segment, transporter.Distance);
            Transporters.Add(transporter);
            return transporter;
        }

        public void RemoveTransporter(Transporter transporter)
        {
            ClearPlan(transporter);
            Transporters.Remove(transporter);
        }

        public Transporter FindTransporter(Vector2 position, float tolerance)
        {
            return Transporters
                .Where(t => Vector2.Distance(t.position, position) <= t.radius + tolerance)
                .OrderBy(t => Vector2.Distance(t.position, position))
                .FirstOrDefault();
        }

        public Depot AddDepot(StyledSegment segment, Vector2 near, int capacity, float speed, float interval, int maxTransporters)
        {
            var depot = new Depot
            {
                Segment = segment, Distance = ProjectDistance(segment, near),
                Capacity = capacity, Speed = speed, Interval = interval, MaxTransporters = maxTransporters
            };
            depot.Position = PositionOn(segment, depot.Distance);
            Depots.Add(depot);
            Adopt(depot);
            return depot;
        }

        // Its transporters out on the road stay there, waiting for work like ones placed by hand.
        public void RemoveDepot(Depot depot)
        {
            Depots.Remove(depot);
            foreach (var t in Transporters.Where(t => t.Home == depot))
            {
                t.Home = null;
                if (t.Returning) ClearPlan(t);
            }
        }

        public Depot FindDepot(Vector2 position, float tolerance)
        {
            return Depots
                .Where(d => Vector2.Distance(d.Position, position) <= DepotRadius + tolerance)
                .OrderBy(d => Vector2.Distance(d.Position, position))
                .FirstOrDefault();
        }

        public void Update(GameTime gameTime, LineDrawing drawing)
        {
            this.drawing = drawing;
            // the lines only change between updates (tools, undo, loading), so these hold for the whole update
            alive = new HashSet<StyledSegment>(drawing.Segments);
            IndexLines();
            resourceIndexStale = true;  // resources may have been added from outside (loading a game)

            Reattach(drawing);
            RescheduleAll();

            if (!Paused)
            {
                float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
                UpdateTimers(drawing, dt);
                Statistics.Advance(dt, Points);
                RescheduleAll();    // a new resource takes over from the first scheduled one
                MoveAll(dt);
                foreach (var transporter in Transporters.ToList())
                    UpdateTransporter(transporter, drawing, dt);
                UpdateDepots(dt);
                RescheduleAll();
                RollIn(dt);
            }

            // follow lines that are being moved
            foreach (var depot in Depots) depot.Position = PositionOn(depot.Segment, depot.Distance);
            foreach (var resource in Resources.Where(r => r.Path == null)) resource.Position = PositionOn(resource.Segment, resource.Distance);
            foreach (var transporter in Transporters) transporter.position = PositionOn(transporter.Segment, transporter.Distance);
            foreach (var transporter in Transporters) Tow(transporter);

            if (!Paused) RemoveStill((float)gameTime.ElapsedGameTime.TotalSeconds);
        }

        #region Green and red lines

        private void UpdateTimers(LineDrawing drawing, float dt)
        {
            foreach (var removed in timers.Keys.Where(s => !alive.Contains(s)).ToList())
                timers.Remove(removed);

            foreach (var segment in drawing.Segments)
            {
                if (Interval(segment) is float interval)
                {
                    timers.TryGetValue(segment, out float time);
                    time = Math.Min(time + dt, interval);

                    // when it can't act yet (green line full / nothing on red line) the timer stays full and fires as soon as it can
                    if (time >= interval && Act(segment))
                        time = 0;

                    timers[segment] = time;
                }

                if (IsSource(segment)) Convey(segment, dt);
            }
        }

        // Output lines produce and consumer lines consume on a timer; input lines are emptied by their output line.
        private float? Interval(StyledSegment segment) =>
            segment.Kind == LineKind.Output ? SpawnInterval : segment.Kind == LineKind.Consumer ? ConsumeInterval : null;

        private bool Act(StyledSegment segment) =>
            segment.Kind == LineKind.Output ? TryProduce(segment) : TryConsume(segment);

        // An output line whose type requires nothing just spawns. Otherwise it needs, for every required type,
        // an input line of its group with a resource that has rolled into place, and consumes one from each.
        private bool TryProduce(StyledSegment segment)
        {
            List<int> required = Recipes.RequirementsOf(segment.Type);
            if (required.Count == 0) return TrySpawn(segment, segment.Type);
            if (segment.Group == 0 || SpawnDistance(segment) == null) return false;

            var taken = new List<Resource>();
            foreach (int type in required)
            {
                Resource resource = InputLinesOf(segment.Group)
                    .Where(s => s.Type == type)
                    .Select(OldestOn)
                    .FirstOrDefault(r => r != null);
                if (resource == null) return false;
                taken.Add(resource);
            }

            foreach (var resource in taken)
            {
                RemoveResource(resource);
                Statistics.Consumed(resource.Type);
            }
            return TrySpawn(segment, segment.Type);
        }

        // Green lines are conveyors: resources queue up toward the B end, one behind the other.
        // Where the queue can reach back to (toward A) and where it starts (at B):
        private (float Start, float End) ConveyorEnds(StyledSegment segment)
        {
            float length = Length(segment);
            float start = Math.Min(ResourceSpacing / 2f, length / 2f);
            return (start, Math.Max(length - ResourceSpacing / 2f, start));
        }

        // A new resource appears right where it belongs: at the end of the queue (at B when the line is empty).
        private bool TrySpawn(StyledSegment segment, int type)
        {
            if (SpawnDistance(segment) is not float at) return false;
            AddResource(new Resource { Type = type, Segment = segment, Distance = at, Position = PositionOn(segment, at) });
            Statistics.Produced(type);
            return true;
        }

        // where the end of the queue is; null when the line is full
        private float? SpawnDistance(StyledSegment segment)
        {
            var (start, end) = ConveyorEnds(segment);
            float nearestA = OnLine(segment).Select(r => r.Distance).Concat(TowedOn(segment)).DefaultIfEmpty(float.NaN).Min();
            float at = float.IsNaN(nearestA) ? end : nearestA - ResourceSpacing;
            return at < start - 0.01f ? null : at;
        }

        // Moves every resource toward B until it reaches the end or the one in front of it.
        // Those that no longer fit (the line got shorter) fall off.
        // Resources being pulled off the line are in the way too, but nothing is pushed back by them.
        private void Convey(StyledSegment segment, float dt)
        {
            var (start, limit) = ConveyorEnds(segment);
            float towed = TowedOn(segment).DefaultIfEmpty(float.MaxValue).Min() - ResourceSpacing;
            foreach (var resource in ResourcesOn(segment))
            {
                if (limit < start - 0.01f)
                {
                    RemoveResource(resource);
                    continue;
                }
                float before = Math.Min(resource.Distance, limit);
                resource.Distance = Math.Min(resource.Distance + ConveyorSpeed * dt, limit);
                if (resource.Distance > towed) resource.Distance = Math.Max(towed, before);
                limit = resource.Distance - ResourceSpacing;
            }
        }

        // where the resources being pulled off a green line still are on it
        private IEnumerable<float> TowedOn(StyledSegment segment) =>
            Transporters.Where(t => t.TowSegment == segment).SelectMany(t => t.Towing).Where(x => x.Resource.Segment == segment).Select(x => x.Resource.Distance);

        // Red lines are queues: deliveries come in at one end and push the resources already there deeper,
        // and the resource that got pushed farthest (the oldest) is consumed at the other end, once it has rolled into its place.
        private bool TryConsume(StyledSegment segment)
        {
            Resource oldest = OldestOn(segment);
            if (oldest == null) return false;
            RemoveResource(oldest);
            Statistics.Consumed(oldest.Type);
            long points = Recipes.PointsOf(oldest.Type);
            Points += points;
            PointsEarned?.Invoke(oldest.Position, points);
            return true;
        }

        // the resource a red line consumes next; null when none has rolled into place
        private Resource OldestOn(StyledSegment segment)
        {
            bool deliveredAtB = SinkLines.TryGetValue(segment, out var line) && line.DeliveredAtB;
            return ResourcesOn(segment).Where(r => r.Path == null)
                .OrderBy(r => deliveredAtB ? r.Distance : -r.Distance).FirstOrDefault();
        }

        // How many resources fit on a green line's conveyor.
        private int ConveyorCapacity(StyledSegment segment)
        {
            var (start, end) = ConveyorEnds(segment);
            return (int)((end - start + 0.01f) / ResourceSpacing) + 1;
        }

        // How many resources a transporter waits for before it goes to a green line: enough to fill it,
        // or as many as the line can ever hold when that is fewer.
        private int PickupNeeded(Transporter t, StyledSegment segment) => Math.Min(t.Capacity - t.Load, ConveyorCapacity(segment));

        // How many empty spaces a red line needs before a transporter goes there: enough for everything it carries,
        // or all the line has when it can never hold that many.
        private int DropNeeded(Transporter t, StyledSegment segment) => Math.Min(t.Load, SlotCount(segment));

        // How many resources fit on a red line, evenly spaced.
        private int SlotCount(StyledSegment segment) => Math.Max(1, (int)((Length(segment) + 0.01f) / ResourceSpacing));

        // Where a red line's place number index is, counting from one of its ends.
        private float SlotDistance(StyledSegment segment, bool fromB, int index)
        {
            float length = Length(segment);
            float distance = length < ResourceSpacing ? length / 2f : ResourceSpacing / 2f + index * ResourceSpacing;
            return fromB ? length - distance : distance;
        }

        private int FreeSlotCount(StyledSegment segment) => Math.Max(0, SlotCount(segment) - OnLine(segment).Count);

        // Unloaded resources roll along their path, then to their spot on the red line.
        private void RollIn(float dt)
        {
            foreach (var resource in Resources.Where(r => r.Path != null))
                Roll(resource, PositionOn(resource.Segment, resource.Distance), resource.Speed * dt);
        }

        // Moves a resource through the points of its path and then to end, at most the given distance.
        // Returns whether it got there; its Path is null from then on.
        private static bool Roll(Resource resource, Vector2 end, float remaining)
        {
            while (true)
            {
                Vector2 next = resource.Path?.Count > 0 ? resource.Path[0] : end;
                float distance = Vector2.Distance(resource.Position, next);
                if (distance > remaining)
                {
                    resource.Position += (next - resource.Position) / distance * remaining;
                    return false;
                }
                resource.Position = next;
                remaining -= distance;
                if (resource.Path?.Count > 0) resource.Path.RemoveAt(0);
                else break;
            }
            resource.Path = null;
            return true;
        }

        // transporters scheduled for it get fixed by the next RescheduleAll
        private void RemoveResource(Resource resource)
        {
            Resources.Remove(resource);
            if (!resourceIndexStale && resource.Segment != null && resourceIndex.TryGetValue(resource.Segment, out var list))
                list.Remove(resource);
        }

        private void AddResource(Resource resource)
        {
            Resources.Add(resource);
            if (resourceIndexStale || resource.Segment == null) return;
            if (!resourceIndex.TryGetValue(resource.Segment, out var list)) resourceIndex[resource.Segment] = list = new List<Resource>();
            list.Add(resource);
        }

        // resources on a line, in no particular order; not to be changed or kept
        private List<Resource> OnLine(StyledSegment segment)
        {
            if (resourceIndexStale)
            {
                foreach (var list in resourceIndex.Values) list.Clear();
                foreach (var resource in Resources)
                {
                    if (resource.Segment == null) continue;
                    if (!resourceIndex.TryGetValue(resource.Segment, out var list)) resourceIndex[resource.Segment] = list = new List<Resource>();
                    list.Add(resource);
                }
                // forget lines that are gone
                foreach (var gone in resourceIndex.Where(e => e.Value.Count == 0 && !alive.Contains(e.Key)).Select(e => e.Key).ToList())
                    resourceIndex.Remove(gone);
                resourceIndexStale = false;
            }
            return resourceIndex.TryGetValue(segment, out var found) ? found : noResources;
        }

        // resources on a line, last (farthest from A) first
        private List<Resource> ResourcesOn(StyledSegment segment)
        {
            return OnLine(segment).OrderByDescending(r => r.Distance).ToList();
        }

        // Keeps SourceLines and SinkLines in sync with the green and red lines.
        // Every green line gives its scheduled transporters its last resources: the first scheduled reserves the last ones
        // (as many as it has room for), the second the ones before them, and so on.
        // Every red line keeps the spaces reserved by transporters heading there within its empty spaces.
        private void RescheduleAll()
        {
            if (drawing == null) return;

            foreach (var (segment, line) in SourceLines.ToList())
            {
                if (alive.Contains(segment) && IsSource(segment)) continue;
                foreach (var t in line.Scheduled.ToList()) ClearPlan(t);
                SourceLines.Remove(segment);
            }
            foreach (var (segment, line) in SinkLines.ToList())
            {
                if (alive.Contains(segment) && IsSink(segment)) continue;
                foreach (var t in line.Scheduled.ToList()) ClearPlan(t);
                SinkLines.Remove(segment);
            }

            foreach (var segment in drawing.Segments.Where(IsSource))
            {
                if (!SourceLines.TryGetValue(segment, out var line)) SourceLines[segment] = line = new SourceLine();
                Reschedule(segment, line);
            }
            foreach (var segment in drawing.Segments.Where(IsSink))
            {
                if (!SinkLines.TryGetValue(segment, out var line)) SinkLines[segment] = line = new SinkLine();
                Reschedule(segment, line);
            }
        }

        private void Reschedule(StyledSegment segment, SourceLine line)
        {
            var onLine = ResourcesOn(segment);
            line.ResourceCount = onLine.Count;
            foreach (var resource in onLine) resource.ReservedBy = null;

            int next = 0;
            for (int i = 0; i < line.Scheduled.Count; i++)
            {
                Transporter t = line.Scheduled[i];
                int room = PickupNeeded(t, segment);
                // not enough resources left to fill it (the line got shorter): it looks for other work
                if (room <= 0 || onLine.Count - next < room)
                {
                    ClearPlan(t);
                    i--;
                    continue;
                }

                var block = onLine.GetRange(next, room);
                foreach (var resource in block) resource.ReservedBy = t;
                next += block.Count;
            }
            line.ReservedCount = next;
        }

        // A line that got shorter can have fewer empty spaces than were reserved: the last scheduled give theirs up
        // and look for a line with room for everything they carry.
        private void Reschedule(StyledSegment segment, SinkLine line)
        {
            line.FreeCount = FreeSlotCount(segment);
            for (int i = line.Scheduled.Count - 1; i >= 0 && line.UnscheduledCount < 0; i--)
                ClearPlan(line.Scheduled[i]);
        }

        #endregion

        #region Transporters

        // Every depot regularly looks for a job (see Plan) for a transporter standing at it. When there is one, the transporter
        // is sent out for it; when not, none is made. It only sends one out when nobody is standing at the depot,
        // and at most one every Interval seconds. Once MaxTransporters of its transporters are out, it waits for one to come back.
        private void UpdateDepots(float dt)
        {
            foreach (var depot in Depots)
            {
                depot.Waiting -= dt;
                if (depot.Waiting > 0) continue;
                depot.Waiting = 0.5f;

                if (Transporters.Count(t => t.Home == depot) >= depot.MaxTransporters) continue;

                float clearance = TransporterRadius * 2f + TransporterGap + depot.Capacity * WagonStep;
                if (Transporters.Any(t => Vector2.Distance(t.position, depot.Position) < clearance)) continue;

                Transporter t = AddTransporter(depot.Segment, depot.Position, depot.Capacity, depot.Speed);
                t.Home = depot;
                Plan(t, drawing);
                if (t.PickupSegment == null) RemoveTransporter(t);
                else depot.Waiting = depot.Interval;  // maybe there's more work: send the next one after its interval
            }
        }

        // An idle transporter from a depot drives home, where it goes back in. On the way it keeps looking for work
        // (unless it is going home to be refitted, see NeedsRefit).
        private void GoHome(Transporter t, LineDrawing drawing)
        {
            Depot home = t.Home;
            if (t.Segment == home.Segment && Math.Abs(t.Distance - home.Distance) < 1f)
            {
                ArriveHome(t, drawing);
                return;
            }

            var legs = RouteToDepot(Search(drawing, t), t, home).Legs;
            if (legs == null) return;   // can't get there (yet): it waits where it is

            t.Route = legs;
            t.Returning = true;
        }

        // The way from where it is to a depot and how far that is; no legs when it can't get there.
        private (float Cost, List<Leg> Legs) RouteToDepot(SearchResult search, Transporter t, Depot depot)
        {
            if (t.Segment == depot.Segment && depot.Distance > t.Distance)
                return (depot.Distance - t.Distance, new List<Leg> { new Leg { Segment = depot.Segment, From = t.Distance, To = depot.Distance } });
            if (!search.Cost.TryGetValue(depot.Segment.A, out float cost))
                return (float.MaxValue, null);
            var legs = LegsTo(search, depot.Segment.A, t.Distance);
            legs.Add(new Leg { Segment = depot.Segment, From = 0, To = depot.Distance });
            return (cost + depot.Distance, legs);
        }

        // At its depot (always empty): it gets the depot's capacity and speed. If the depot may have it out, it goes back to work
        // when there is a job for it; otherwise it goes in.
        private void ArriveHome(Transporter t, LineDrawing drawing)
        {
            Depot home = t.Home;
            t.Capacity = home.Capacity;
            t.speed = home.Speed;
            t.FirstLoaded = 0;

            if (Transporters.Count(o => o.Home == home && o != t) < home.MaxTransporters)
            {
                Plan(t, drawing);
                if (t.PickupSegment != null) return;
            }
            RemoveTransporter(t);
        }

        // Its capacity or speed is not what its depot gives its transporters (they were changed, or it was adopted):
        // when it is empty and idle it drives home to get them.
        private static bool NeedsRefit(Transporter t) =>
            t.Home != null && (t.Capacity != t.Home.Capacity || t.speed != t.Home.Speed);

        // An idle transporter that doesn't belong to a depot goes to the nearest one it can get to and becomes one of its transporters
        // (which takes it out of the game, unless that depot has room for it and work to do).
        private void JoinNearestDepot(Transporter t, LineDrawing drawing)
        {
            var search = Search(drawing, t);
            Depot nearest = Depots
                .Select(d => (Depot: d, RouteToDepot(search, t, d).Cost))
                .Where(d => d.Cost < float.MaxValue)
                .OrderBy(d => d.Cost)
                .Select(d => d.Depot)
                .FirstOrDefault();
            if (nearest == null) return;    // none it can get to (yet): it waits where it is

            t.Home = nearest;
            GoHome(t, drawing);
        }

        // A new depot takes in transporters that don't belong to any depot, nearest first, as many as it may have out.
        // Ones that differ from what it gives its transporters come to it to be refitted once they are idle.
        private void Adopt(Depot depot)
        {
            var homeless = Transporters
                .Where(t => t.Home == null)
                .OrderBy(t => Vector2.Distance(t.position, depot.Position))
                .Take(depot.MaxTransporters);
            foreach (var t in homeless) t.Home = depot;
        }

        private void UpdateTransporter(Transporter t, LineDrawing drawing, float dt)
        {
            // still on its way (it moved in MoveAll)
            if (t.Route.Count > 0)
            {
                ResolveBlock(t, dt);
                if (t.Returning) LookForWorkOnTheWayHome(t, drawing, dt);
                return;
            }
            t.Blocker = null;
            t.BlockedTime = 0;

            if (t.Returning)
            {
                t.Returning = false;
                if (t.Home != null) GoHome(t, drawing);    // arrived: goes in
                return;
            }

            // arrived (or idle)
            if (t.PickupSegment != null)
            {
                if (t.Segment != t.Stop) ClearPlan(t);
                else if (!Load(t)) return;      // nothing reserved for it yet
                else HeadForDrop(t, drawing);   // hooked: it drives straight on to the red line reserved for them, pulling them along
            }
            else if (t.DropSegment != null)
            {
                if (t.Segment != t.Stop) ClearPlan(t);
                else if (!Unload(t)) return;    // red line full: wait until it consumes one
                else ClearPlan(t);              // done here; whatever is left goes to another red line
            }

            t.Waiting -= dt;
            if (t.Waiting <= 0 && t.Route.Count == 0)
            {
                t.Waiting = 0.5f;
                if (NeedsRefit(t) && !t.Carrying)
                {
                    GoHome(t, drawing);
                    if (t.Route.Count > 0 || !Transporters.Contains(t)) return;
                    // can't get home: it keeps working as it is
                }

                Plan(t, drawing);
                // finished its job and found no new one
                if (t.Route.Count == 0 && !t.Carrying)
                {
                    if (t.Home != null) GoHome(t, drawing);
                    else JoinNearestDepot(t, drawing);
                }
            }
        }

        private void LookForWorkOnTheWayHome(Transporter t, LineDrawing drawing, float dt)
        {
            if (NeedsRefit(t)) return;
            t.Waiting -= dt;
            if (t.Waiting > 0) return;
            t.Waiting = 0.5f;

            var home = t.Route;
            t.Route = new List<Leg>();
            Plan(t, drawing);
            if (t.PickupSegment != null) t.Returning = false;
            else t.Route = home;
        }

        // Done loading: it gives up its place on the green line and keeps the empty spaces it reserved on the red line,
        // routing there from where it is. When it can't get there any more (the lines changed), it plans again.
        private void HeadForDrop(Transporter t, LineDrawing drawing)
        {
            if (SourceLines.TryGetValue(t.PickupSegment, out var line)) line.Scheduled.Remove(t);
            t.PickupSegment = null;
            t.Route.Clear();
            t.Stop = null;
            t.DropReserved = Math.Min(t.DropReserved, t.Load);

            var route = t.DropSegment == null || t.DropReserved == 0 ? default
                : RouteToEntrance(Search(drawing, t), t.Distance, t.DropSegment, t.DropSegment.A, t.DropSegment.B);
            if (route.Legs == null)
            {
                ClearPlan(t);
                return;
            }
            t.Route = route.Legs;
            t.Stop = route.Legs[^1].Segment;
        }

        // Stopped with its first wagon at the B end of the green line, where its resources queue up: it hooks the string of resources
        // reserved for it and doesn't wait. They are pulled off the line through B as it drives on (see Tow).
        // The one nearest B goes onto the first wagon, the next onto the second, and so on. Returns false while none are reserved for it yet.
        private bool Load(Transporter t)
        {
            var block = ResourcesOn(t.PickupSegment).Where(r => r.ReservedBy == t).ToList();
            if (block.Count == 0) return false;

            float length = Length(t.PickupSegment);
            t.TowSegment = t.PickupSegment;
            t.TowEntrance = Vector2.Distance(t.position, t.PickupSegment.B.position);
            t.Towed = 0;
            t.TowLength = 0;
            for (int i = 0; i < block.Count; i++)
            {
                Resource resource = block[i];
                RemoveResource(resource);
                resource.ReservedBy = null;
                resource.Path = null;
                int wagon = t.FirstLoaded + t.Load + i;
                float from = t.TowEntrance + length - resource.Distance;
                t.Towing.Add((resource, wagon, from));
                t.TowLength = Math.Max(t.TowLength, Math.Max(from, FirstWagonOffset + wagon * WagonStep) - t.TowEntrance);
            }
            t.Load += block.Count;
            t.CargoType = SourceType(t.PickupSegment);
            return true;
        }

        // Places the resources a transporter is pulling. Each one is a distance behind it, measured back along the way it came,
        // through the B end of the green line and on along that line: while it drives TowLength, that distance goes evenly
        // from where the resource lay on the line to where its wagon is. The wagons themselves follow the transporter as always,
        // so by then every resource is on its wagon.
        private void Tow(Transporter t)
        {
            if (t.Towing.Count == 0) return;

            float done = t.TowLength <= 0 ? 1f : Math.Min(1f, t.Towed / t.TowLength);
            float length = Length(t.TowSegment);
            var path = BackPath(t);
            foreach (var (resource, wagon, from) in t.Towing)
            {
                float behind = MathHelper.Lerp(from, FirstWagonOffset + wagon * WagonStep, done);
                float onLine = behind - (t.Towed + t.TowEntrance);  // how far it still is from B, back along the green line
                if (onLine > 0)
                {
                    resource.Segment = t.TowSegment;
                    resource.Distance = Math.Max(0, length - onLine);
                    resource.Position = PositionOn(t.TowSegment, resource.Distance);
                }
                else
                {
                    resource.Segment = null;
                    resource.Position = PointAlong(path, behind);
                }
            }
            if (done >= 1f) StopTowing(t);
        }

        private static void StopTowing(Transporter t)
        {
            foreach (var (resource, _, _) in t.Towing) resource.Segment = null;
            t.Towing.Clear();
            t.TowSegment = null;
        }

        // How fast resources roll on and off a transporter: as fast as it drives, or as the slower one it was following.
        private static float TransferSpeed(Transporter t) => t.CurrentSpeed > 0 ? t.CurrentSpeed : t.speed;

        // Stopped with its first wagon at the entrance of the red line: the loaded wagons let go of their resources,
        // which roll along the way the transporter came, through the entrance, to the places next to it,
        // pushing the resources already on the line deeper. The one in front goes deepest, so they don't cross.
        // Returns whether everything reserved there has been unloaded.
        private bool Unload(Transporter t)
        {
            var path = BackPath(t);
            Vector2 entrance = PointAlong(path, FirstWagonOffset);
            float entranceDistance = ProjectDistance(t.DropSegment, entrance);
            bool atB = entranceDistance > Length(t.DropSegment) / 2f;
            int count = Math.Min(t.DropReserved, FreeSlotCount(t.DropSegment));
            if (count == 0) return false;
            float speed = TransferSpeed(t);

            // the ones already there get pushed deeper, keeping their order
            var stored = ResourcesOn(t.DropSegment).OrderBy(r => Math.Abs(r.Distance - entranceDistance)).ToList();
            for (int i = 0; i < stored.Count; i++)
            {
                float distance = SlotDistance(t.DropSegment, atB, count + i);
                if (Math.Abs(stored[i].Distance - distance) < 0.01f) continue;
                stored[i].Distance = distance;
                stored[i].Path ??= new List<Vector2>();
                stored[i].Speed = speed;
            }

            for (int i = 0; i < count; i++)
            {
                float along = FirstWagonOffset + (t.FirstLoaded + i) * WagonStep;
                var waypoints = new List<Vector2>();
                for (int j = path.Count - 1; j >= 0; j--)
                    if (path[j].Along < along && path[j].Along > FirstWagonOffset) waypoints.Add(path[j].Point);
                waypoints.Add(entrance);
                float distance = SlotDistance(t.DropSegment, atB, count - 1 - i);
                // one still being pulled toward its wagon starts from where it is
                var towed = t.Towing.FirstOrDefault(x => x.Wagon == t.FirstLoaded + i).Resource;
                Vector2 start = towed?.Position ?? PointAlong(path, along);
                AddResource(new Resource { Type = t.CargoType, Segment = t.DropSegment, Distance = distance, Position = start, Path = waypoints, Speed = speed });
            }
            t.Towing.RemoveAll(x => x.Wagon < t.FirstLoaded + count);
            if (t.Towing.Count == 0) StopTowing(t);

            t.FirstLoaded += count;
            t.Load -= count;
            if (t.Load == 0) t.FirstLoaded = 0;
            t.DropReserved -= count;
            if (SinkLines.TryGetValue(t.DropSegment, out var line))
            {
                line.FreeCount -= count;
                line.DeliveredAtB = atB;
            }
            Statistics.Transported(t.CargoType, count);
            if (t.DropReserved == 0) Statistics.Trip();    // a trip ends when the whole delivery is unloaded
            return t.DropReserved == 0;
        }

        // Empty: looks for a whole job: resources that need taking somewhere, and a place to take them (see PlanJob).
        // Which green line it picks up from goes by the type of resource, in this order:
        //   1. types no factory takes (no input line of that type): to a consumer line
        //   2. types a factory takes: to an input line with room for them
        //   3. types a factory takes, but no input line of that type has room: to a consumer line
        // Carrying: takes its load to an input line, or when none has room for it, to a consumer line (see PlanDrop).
        private void Plan(Transporter t, LineDrawing drawing)
        {
            var search = Search(drawing, t);
            static bool ToFactory(StyledSegment s) => s.Kind == LineKind.Input;
            static bool ToConsumer(StyledSegment s) => s.Kind == LineKind.Consumer;

            if (!t.Carrying)
            {
                var needed = drawing.Segments.Where(ToFactory).Select(s => s.Type).ToHashSet();
                // types some input line still has unscheduled empty spaces for
                var factoryHasRoom = SinkLines.Where(s => ToFactory(s.Key) && s.Value.UnscheduledCount > 0).Select(s => s.Key.Type).ToHashSet();
                if (!PlanJob(t, drawing, search, ToConsumer, type => !needed.Contains(type))
                    && !PlanJob(t, drawing, search, ToFactory, _ => true))
                    PlanJob(t, drawing, search, ToConsumer, type => needed.Contains(type) && !factoryHasRoom.Contains(type));
            }
            else if (!PlanDrop(t, search, ToFactory))
                PlanDrop(t, search, ToConsumer);
        }

        // A green line holding one of the given types with enough unreserved resources to fill it (see PickupNeeded), and one of
        // the given red lines taking their type with enough unscheduled empty spaces for them (or all it has, when it can never
        // hold that many). It picks the job with the shortest drive there and on to the red line, and reserves both:
        // the green line's last resources and the red line's empty spaces. Returns whether it found one.
        // Consumer lines go by Fullness first: the least full one wins, and the drive only decides between equally full ones.
        private bool PlanJob(Transporter t, LineDrawing drawing, SearchResult search, Func<StyledSegment, bool> sinks, Func<int, bool> types)
        {
            (int Fill, float Cost, List<Leg> Legs) best = (int.MaxValue, float.MaxValue, null);
            bool Better(int fill, float cost) => fill < best.Fill || (fill == best.Fill && cost < best.Cost);
            (StyledSegment Pickup, SourceLine Line, int Need, StyledSegment Drop, SinkLine DropLine)? job = null;
            foreach (var (segment, line) in SourceLines)
            {
                int type = SourceType(segment);
                if (!types(type)) continue;
                int unreserved = ResourcesOn(segment).Count - line.ReservedCount;
                int need = PickupNeeded(t, segment);
                if (need <= 0 || unreserved < need) continue;
                var drops = SinkLines
                    .Where(s => sinks(s.Key) && Accepts(s.Key, type) && s.Value.UnscheduledCount >= Math.Min(need, SlotCount(s.Key)))
                    .ToList();
                if (drops.Count == 0) continue;

                var pickup = RouteToEntrance(search, t.Distance, segment, segment.B);
                if (pickup.Legs == null || !Better(drops.Min(d => Fullness(d.Key, d.Value)), pickup.Cost)) continue;

                // on from where it will stand loading
                Leg stop = pickup.Legs[^1];
                var fromPickup = Search(drawing, t, stop.Segment, stop.To);
                foreach (var (sink, dropLine) in drops)
                {
                    int fill = Fullness(sink, dropLine);
                    if (!Better(fill, pickup.Cost)) continue;
                    var delivery = RouteToEntrance(fromPickup, stop.To, sink, sink.A, sink.B);
                    if (delivery.Legs == null || !Better(fill, pickup.Cost + delivery.Cost)) continue;
                    best = (fill, pickup.Cost + delivery.Cost, pickup.Legs);
                    job = (segment, line, need, sink, dropLine);
                }
            }
            if (job is not var (pickupSegment, sourceLine, needed, dropSegment, sinkLine)) return false;

            t.PickupSegment = pickupSegment;
            sourceLine.Scheduled.Add(t);
            sourceLine.ReservedCount += needed;  // for the others planning this frame
            t.DropSegment = dropSegment;
            t.DropReserved = Math.Min(needed, SlotCount(dropSegment));
            sinkLine.Scheduled.Add(t);
            t.Route = best.Legs;
            t.Stop = best.Legs[^1].Segment;
            return true;
        }

        // Schedules on the nearest of the given red lines taking its type with enough unscheduled empty spaces for its whole load
        // (see DropNeeded), reserving them. Consumer lines go by Fullness first, like in PlanJob. Returns whether it found one.
        private bool PlanDrop(Transporter t, SearchResult search, Func<StyledSegment, bool> sinks)
        {
            (int Fill, float Cost, List<Leg> Legs) best = (int.MaxValue, float.MaxValue, null);
            SinkLine dropLine = null;
            foreach (var (segment, line) in SinkLines)
            {
                if (!sinks(segment) || !Accepts(segment, t.CargoType) || line.UnscheduledCount < DropNeeded(t, segment)) continue;
                int fill = Fullness(segment, line);
                if (fill > best.Fill) continue;

                var route = RouteToEntrance(search, t.Distance, segment, segment.A, segment.B);
                if (route.Legs != null && (fill < best.Fill || route.Cost < best.Cost))
                {
                    best = (fill, route.Cost, route.Legs);
                    t.DropSegment = segment;
                    dropLine = line;
                }
            }
            if (dropLine == null) return false;

            t.DropReserved = DropNeeded(t, t.DropSegment);
            dropLine.Scheduled.Add(t);
            t.Route = best.Legs;
            t.Stop = best.Legs[^1].Segment;
            return true;
        }

        // How full a consumer line is or will be: the resources on it plus the spaces scheduled for deliveries.
        // 0 for other red lines, so the nearest of them is picked.
        private int Fullness(StyledSegment segment, SinkLine line) =>
            segment.Kind == LineKind.Consumer ? OnLine(segment).Count + line.ReservedCount : 0;

        // Every line has a list of the transporters on it, front first (farthest along the way they drive).
        // They move in that order, so each one sees where the one in front of it has got to this frame.
        // One held up by a transporter on another line (around a corner, past a junction) lets that one move first.
        private void MoveAll(float dt)
        {
            occupied = Transporters.ToDictionary(t => t, Occupied);
            var moved = new HashSet<Transporter>();
            void Visit(Transporter t)
            {
                if (t.Route.Count == 0 || !moved.Add(t)) return;
                FreeDistance(t, t.speed * dt, out Transporter inTheWay);
                if (inTheWay != null) Visit(inTheWay);
                Move(t, dt);
            }

            foreach (var line in Transporters.GroupBy(t => t.Segment).ToList())
            {
                foreach (var t in line.OrderByDescending(Progress).ToList())
                    Visit(t);
            }
        }

        // how far along its line a transporter is, in the direction it drives
        private static float Progress(Transporter t)
        {
            Leg leg = t.Route.FirstOrDefault();
            bool towardA = leg != null && leg.Segment == t.Segment && Math.Min(leg.To, Length(t.Segment)) < t.Distance;
            return towardA ? Length(t.Segment) - t.Distance : t.Distance;
        }

        // Drives as far as it wants this frame, or, when that would bring it too close to the next transporter ahead,
        // up to just behind that one's last wagon. At the end of a line it goes on to the next line of its route.
        private void Move(Transporter t, float dt)
        {
            float allowed = FreeDistance(t, t.speed * dt, out Transporter blocker);
            // following a slower one is fine; only standing still counts as blocked (see ResolveBlock)
            t.Blocker = allowed < 0.001f ? blocker : null;
            t.CurrentSpeed = blocker != null ? Math.Min(t.speed, TransferSpeed(blocker)) : t.speed;
            t.Towed += Advance(t, allowed);
            t.position = PositionOn(t.Segment, t.Distance);
            occupied[t] = Occupied(t);
        }

        // where a transporter's body and wagons are, and the box around their centers (so whole transporters far away are skipped)
        private class Occupancy
        {
            public List<(Vector2 Position, float Radius)> Places;
            public Vector2 Min, Max;
        }

        private Occupancy Occupied(Transporter t)
        {
            var places = new List<(Vector2, float)> { (t.position, t.radius) };
            Vector2 min = t.position, max = t.position;
            foreach (var wagon in WagonPositions(t))
            {
                places.Add((wagon, ResourceRadius));
                min = Vector2.Min(min, wagon);
                max = Vector2.Max(max, wagon);
            }
            return new Occupancy { Places = places, Min = min, Max = max };
        }

        // How far it can drive along its route, at most wanted: along each line of the route it looks for the nearest
        // body or wagon of another transporter ahead of it, and stops that far minus the room both need.
        private float FreeDistance(Transporter t, float wanted, out Transporter blocker)
        {
            float free = wanted;
            Transporter by = null;
            void Limit(float at, Transporter owner)
            {
                if (at >= free) return;
                free = Math.Max(0, at);
                by = owner;
            }

            float reach = t.radius + Math.Max(TransporterRadius, ResourceRadius) + TransporterGap;
            float walked = 0;               // along the route, to where this leg starts
            StyledSegment segment = t.Segment;
            float distance = t.Distance;
            bool first = true;

            foreach (Leg leg in t.Route)
            {
                if (walked - reach > free) break;
                float length = Length(leg.Segment);

                if (segment != leg.Segment)
                {
                    segment = leg.Segment;
                    distance = Math.Min(leg.From, length);
                    first = false;
                }

                float to = Math.Min(leg.To, length);
                float span = Math.Abs(to - distance);
                int sign = Math.Sign(to - distance);
                if (sign != 0)
                {
                    // only something within this box can hold it up: the part of the line ahead of it that it may still
                    // reach (see Limit), plus the room both need; the cheap box test skips most places
                    float window = Math.Max(0, Math.Min(span, free - walked)) + reach;
                    Vector2 near = PositionOn(segment, distance), far = PositionOn(segment, distance + sign * window);
                    Vector2 min = Vector2.Min(near, far) - new Vector2(0.6f), max = Vector2.Max(near, far) + new Vector2(0.6f);

                    foreach (var (other, occupancy) in occupied)
                    {
                        if (other == t || occupancy.Max.X < min.X || occupancy.Min.X > max.X || occupancy.Max.Y < min.Y || occupancy.Min.Y > max.Y) continue;
                        foreach (var (position, radius) in occupancy.Places)
                        {
                            if (position.X < min.X || position.X > max.X || position.Y < min.Y || position.Y > max.Y) continue;
                            if (!IsOn(segment, position)) continue;
                            float ahead = (ProjectDistance(segment, position) - distance) * sign;
                            float clearance = t.radius + radius + TransporterGap;
                            if (ahead < 0 || (first && ahead == 0) || ahead > span + clearance) continue;
                            Limit(walked + ahead - clearance, other);
                        }
                    }
                }

                walked += span;
                distance = to;
            }

            blocker = by;
            return free;
        }

        // on the line and between its ends (something just past an end is on the next line, not at this one's end)
        private static bool IsOn(StyledSegment segment, Vector2 position)
        {
            Vector2 a = segment.A.position, b = segment.B.position;
            float length = Vector2.Distance(a, b);
            if (length <= 0) return Vector2.Distance(position, a) < 0.5f;
            float along = Vector2.Dot(position - a, (b - a) / length);
            return along >= -0.01f && along <= length + 0.01f && LineDrawing.DistanceToSegment(a, b, position) < 0.5f;
        }

        // returns how far it drove
        private float Advance(Transporter t, float remaining)
        {
            float wanted = remaining;
            while (t.Route.Count > 0)
            {
                Leg leg = t.Route[0];
                float length = Length(leg.Segment);
                if (t.Segment != leg.Segment)
                {
                    RecordTrail(t);     // the corner
                    t.Segment = leg.Segment;
                    t.Distance = Math.Min(leg.From, length);
                }

                float to = Math.Min(leg.To, length);
                float diff = to - t.Distance;
                if (Math.Abs(diff) > remaining)
                {
                    t.Distance += Math.Sign(diff) * remaining;
                    remaining = 0;
                    break;
                }
                t.Distance = to;
                remaining -= Math.Abs(diff);
                t.Route.RemoveAt(0);
            }
            RecordTrail(t);
            return wanted - remaining;
        }


        private static bool IsIdle(Transporter t) =>
            t.Route.Count == 0 && t.PickupSegment == null && t.DropSegment == null;

        // standing still, so routes should avoid its line
        private static bool IsStopped(Transporter t) => t.Route.Count == 0 || t.BlockedTime > 0.2f;

        // Sorts out a transporter that can't move on:
        // an idle one in the way drives off; of two blocking each other, the later placed backs off;
        // stuck for long behind a working one, it gives up its job and plans again, avoiding lines where transporters stand.
        private void ResolveBlock(Transporter t, float dt)
        {
            Transporter blocker = t.Blocker;
            if (blocker == null)
            {
                t.BlockedTime = 0;
                return;
            }

            t.BlockedTime += dt;
            if (IsIdle(blocker))
            {
                if (t.BlockedTime > 0.5f) MakeWay(blocker, t);
            }
            else if (blocker.Blocker == t && Transporters.IndexOf(t) > Transporters.IndexOf(blocker))
            {
                if (t.BlockedTime > 0.3f)
                {
                    ClearPlan(t);
                    MakeWay(t, blocker);
                }
            }
            else if (t.BlockedTime > 4f)
            {
                ClearPlan(t);
                t.Waiting = 0.5f + (float)random.NextDouble();
            }
        }

        // Drives an idle transporter out of another's way: to the end of its line away from the other one,
        // or, when it is already there, a bit onto a line leaving from there (preferring one nobody is on).
        private void MakeWay(Transporter t, Transporter from)
        {
            Vector2 a = t.Segment.A.position, b = t.Segment.B.position;
            bool toA = IsBidirectional(t.Segment) && Vector2.Distance(a, from.position) > Vector2.Distance(b, from.position);
            float end = toA ? 0 : Length(t.Segment);
            t.Waiting = 0.5f;
            t.BlockedTime = 0;

            if (Math.Abs(t.Distance - end) > 1f)
            {
                t.Route.Add(new Leg { Segment = t.Segment, From = t.Distance, To = end });
                return;
            }

            Point point = toA ? t.Segment.A : t.Segment.B;
            StyledSegment next = RoadsFrom(point)
                .Where(s => s != t.Segment)
                .OrderBy(s => Transporters.Count(o => o != t && o.Segment == s))
                .FirstOrDefault();
            if (next == null) return;

            float into = Math.Min(Length(next), t.radius * 2f + t.Capacity * WagonStep);
            bool fromA = next.A == point;
            t.Route.Add(new Leg { Segment = next, From = fromA ? 0 : float.PositiveInfinity, To = fromA ? into : Length(next) - into });
        }

        // Adds where the transporter is now, and forgets what is farther back than its last wagon.
        private void RecordTrail(Transporter t)
        {
            t.Trail.Add((t.Segment, t.Distance));

            float keep = FirstWagonOffset + t.Capacity * WagonStep + 10f;
            foreach (var (_, _, from) in t.Towing) keep = Math.Max(keep, from + 10f);   // the way back to the green line it is pulling from
            float walked = 0;
            Vector2 previous = PositionOn(t.Segment, t.Distance);
            for (int i = t.Trail.Count - 1; i > 0; i--)
            {
                Vector2 p = PositionOn(t.Trail[i].Segment, t.Trail[i].Distance);
                walked += Vector2.Distance(previous, p);
                previous = p;
                if (walked > keep)
                {
                    t.Trail.RemoveRange(0, i);
                    break;
                }
            }
        }

        // A transporter that hasn't moved for MaxStillTime seconds (stuck, or waiting with nothing to do) is removed,
        // together with what it carries. Any movement, also of the line it stands on, starts the count again.
        private void RemoveStill(float dt)
        {
            foreach (var t in Transporters.ToList())
            {
                if (Vector2.DistanceSquared(t.position, t.LastPosition) > 0.0001f)
                {
                    t.LastPosition = t.position;
                    t.StillTime = 0;
                }
                else if ((t.StillTime += dt) >= MaxStillTime)
                {
                    RemoveTransporter(t);
                }
            }
        }

        private void ClearPlan(Transporter t)
        {
            t.Route.Clear();
            if (t.PickupSegment != null && SourceLines.TryGetValue(t.PickupSegment, out var line))
                line.Scheduled.Remove(t);
            t.PickupSegment = null;
            if (t.DropSegment != null && SinkLines.TryGetValue(t.DropSegment, out var sink))
                sink.Scheduled.Remove(t);
            t.DropSegment = null;
            t.Stop = null;
            t.DropReserved = 0;
            t.Waiting = 0;
            t.Returning = false;
        }

        #endregion

        #region Path finding

        private class SearchResult
        {
            public Dictionary<Point, float> Cost = new Dictionary<Point, float>();
            public Dictionary<Point, (Point From, StyledSegment Via)> Previous = new Dictionary<Point, (Point, StyledSegment)>();
        }

        // Dijkstra from where a transporter is to every point it can reach, following the allowed directions.
        // Lines where other transporters stand cost extra, so it drives around them when it can.
        private SearchResult Search(LineDrawing drawing, Transporter self) => Search(drawing, self, self.Segment, self.Distance);

        // the same from somewhere else it will be (like where it stops to load)
        private SearchResult Search(LineDrawing drawing, Transporter self, StyledSegment segment, float distance)
        {
            var stopped = new HashSet<StyledSegment>(Transporters.Where(o => o != self && IsStopped(o)).Select(o => o.Segment));
            float Cost(StyledSegment s) => Length(s) + (stopped.Contains(s) ? StoppedPenalty : 0);

            var result = new SearchResult();
            var queue = new PriorityQueue<Point, float>();
            void Relax(Point point, float cost, Point from, StyledSegment via)
            {
                if (result.Cost.TryGetValue(point, out float old) && old <= cost) return;
                result.Cost[point] = cost;
                result.Previous[point] = (from, via);
                queue.Enqueue(point, cost);
            }

            Relax(segment.B, Length(segment) - distance, null, segment);
            if (IsBidirectional(segment)) Relax(segment.A, distance, null, segment);

            while (queue.TryDequeue(out Point point, out float cost))
            {
                if (cost > result.Cost[point]) continue;
                foreach (var s in RoadsFrom(point))
                    Relax(s.B, cost + Cost(s), point, s);
            }
            return result;
        }

        // Lookups over the lines that hold for a whole update (the lines only change between updates).
        private void IndexLines()
        {
            roadsFrom = new Dictionary<Point, List<StyledSegment>>();
            inputLines = new Dictionary<int, List<StyledSegment>>();
            foreach (var s in drawing.Segments)
            {
                if (IsWalkable(s))
                {
                    if (!roadsFrom.TryGetValue(s.A, out var roads)) roadsFrom[s.A] = roads = new List<StyledSegment>();
                    roads.Add(s);
                }
                else if (s.Kind == LineKind.Input && s.Group != 0)
                {
                    if (!inputLines.TryGetValue(s.Group, out var inputs)) inputLines[s.Group] = inputs = new List<StyledSegment>();
                    inputs.Add(s);
                }
            }
        }

        private List<StyledSegment> RoadsFrom(Point point) => roadsFrom.TryGetValue(point, out var list) ? list : noRoads;

        // input lines built together with an output line (same Group), in drawing order
        private List<StyledSegment> InputLinesOf(int group) => inputLines.TryGetValue(group, out var list) ? list : noRoads;

        // Where to stop to load or unload at a green or red line: having driven to one of the given ends along a road,
        // a bit onto a road going on from there, just far enough that its first wagon is at that end.
        // It never turns back onto the road it came by. Lines themselves are never driven on, so where no road goes on
        // from those ends, the line can't be served.
        private (float Cost, List<Leg> Legs) RouteToEntrance(SearchResult search, float startDistance, StyledSegment segment, params Point[] ends)
        {
            (float Cost, List<Leg> Legs) best = (float.MaxValue, null);
            foreach (Point end in ends)
            {
                if (!search.Cost.TryGetValue(end, out float cost)) continue;
                StyledSegment arrivedBy = search.Previous[end].Via;
                if (arrivedBy == segment) continue;

                foreach (var road in RoadsFrom(end))
                {
                    if (road == arrivedBy) continue;

                    float into = Math.Min(FirstWagonOffset, Length(road));
                    if (cost + into >= best.Cost) continue;
                    var legs = LegsTo(search, end, startDistance);
                    legs.Add(new Leg { Segment = road, From = 0, To = into });
                    best = (cost + into, legs);
                }
            }
            return best;
        }

        private static List<Leg> LegsTo(SearchResult search, Point point, float startDistance)
        {
            var legs = new List<Leg>();
            while (true)
            {
                var (from, via) = search.Previous[point];
                float end = point == via.A ? 0 : float.PositiveInfinity;
                if (from == null)
                {
                    legs.Add(new Leg { Segment = via, From = startDistance, To = end });
                    break;
                }
                legs.Add(new Leg { Segment = via, From = from == via.A ? 0 : float.PositiveInfinity, To = end });
                point = from;
            }
            legs.Reverse();
            return legs;
        }

        #endregion

        #region Helpers

        private static float Length(StyledSegment segment) => Vector2.Distance(segment.A.position, segment.B.position);

        public static Vector2 PositionOn(StyledSegment segment, float distance)
        {
            float length = Length(segment);
            if (length <= 0) return segment.A.position;
            return Vector2.Lerp(segment.A.position, segment.B.position, MathHelper.Clamp(distance / length, 0f, 1f));
        }

        private static float ProjectDistance(StyledSegment segment, Vector2 position)
        {
            if (segment.A.position == segment.B.position) return 0;
            float t = MathHelper.Clamp(VectorHelpers.ProjectionFactor(segment.A.position, segment.B.position, position), 0f, 1f);
            return t * Length(segment);
        }

        // Lines get replaced when they are split, erased, cleared or restored by undo.
        // Dots on a line that no longer exists move to the line now under them, or disappear.
        private void Reattach(LineDrawing drawing)
        {
            foreach (var depot in Depots.Where(d => !alive.Contains(d.Segment)).ToList())
            {
                StyledSegment segment = drawing.FindSegment(depot.Position, 2f);
                if (segment == null || !IsWalkable(segment)) { RemoveDepot(depot); continue; }
                depot.Segment = segment;
                depot.Distance = ProjectDistance(segment, depot.Position);
            }

            foreach (var resource in Resources.Where(r => !alive.Contains(r.Segment)).ToList())
            {
                StyledSegment segment = resource.Path == null ? drawing.FindSegment(resource.Position, 2f) : null;
                if (segment == null) { RemoveResource(resource); continue; }
                resource.Segment = segment;
                resource.Distance = ProjectDistance(segment, resource.Position);
            }
            resourceIndexStale = true;  // some may have moved to another line

            foreach (var t in Transporters.ToList())
            {
                t.Trail.RemoveAll(p => !alive.Contains(p.Segment));
                if (t.TowSegment != null && !alive.Contains(t.TowSegment)) StopTowing(t);  // what it was pulling is on its wagons at once
                if (!alive.Contains(t.Segment))
                {
                    StyledSegment segment = drawing.FindSegment(t.position, 2f);
                    if (segment == null) { RemoveTransporter(t); continue; }
                    t.Segment = segment;
                    t.Distance = ProjectDistance(segment, t.position);
                    ClearPlan(t);
                }
                else if (t.Route.Any(leg => !alive.Contains(leg.Segment)) || (t.DropSegment != null && !alive.Contains(t.DropSegment)))
                {
                    ClearPlan(t);
                }
            }
        }

        #endregion

        #region Drawing

        public void Draw(LineDrawing drawing)
        {
            foreach (var segment in drawing.Segments)
            {
                if (!IsBidirectional(segment)) DrawDirection(segment);
                if (timers.TryGetValue(segment, out float time))
                {
                    bool source = IsSource(segment);
                    float progress = time / (source ? SpawnInterval : ConsumeInterval);
                    bool consumeAtA = !source && SinkLines.TryGetValue(segment, out var line) && line.DeliveredAtB;
                    DrawTimer(source || consumeAtA ? segment.A.position : segment.B.position, progress, segment.Color);
                }
            }

            // under the transporters coming out of it, with how many of its transporters are out / how many it may have out
            foreach (var depot in Depots)
            {
                DrawDepot(depot.Position, 1f);
                int outside = Transporters.Count(t => t.Home == depot);
                DrawLabel(depot.Position - new Vector2(0, DepotRadius + 12f),
                    (outside.ToString(), TransporterColor), ("/", Color.White * 0.5f), (depot.MaxTransporters.ToString(), Color.White));
            }

            foreach (var resource in Resources)
            {
                if (resource.ReservedBy != null)
                    DrawShape.DrawCircle(resource.Position, ResourceRadius + 3f, 1.5f, TransporterColor * 0.7f);
                DrawShape.DrawFilledCircle(resource.Position, ResourceRadius, 1.5f, ResourceColorOf(resource.Type), Color.Black);
            }

            foreach (var t in Transporters)
            {
                // last wagon first, so each one overlaps the one behind it
                var wagons = WagonPositions(t);
                for (int i = wagons.Count - 1; i >= 0; i--)
                    DrawWagon(wagons[i], i >= t.FirstLoaded && i < t.FirstLoaded + t.Load && !t.Towing.Any(b => b.Wagon == i), 1f, t.CargoType);
                foreach (var (resource, _, _) in t.Towing)
                    DrawShape.DrawFilledCircle(resource.Position, ResourceRadius, 1.5f, ResourceColorOf(resource.Type), Color.Black);
                DrawShape.DrawFilledCircle(t.position, t.radius, 2f, t.color, Color.White);
            }
        }

        // a dark dot with a thick ring in the transporters' color and a small one in the middle
        public void DrawDepot(Vector2 center, float alpha)
        {
            DrawShape.DrawFilledCircle(center, DepotRadius, 3f, new Color(30, 30, 36) * alpha, TransporterColor * alpha);
            DrawShape.DrawFilledCircle(center, 3.5f, 0f, TransporterColor * alpha, Color.Transparent);
        }

        // Distance from a transporter's center to its first wagon, and between wagons.
        // (the first overlaps the transporter as much as wagons overlap each other)
        public float FirstWagonOffset => TransporterRadius + ResourceSpacing - ResourceRadius;
        public float WagonStep => ResourceSpacing;

        // a carried resource of the given type, or a faint ring for an empty place
        public void DrawWagon(Vector2 position, bool loaded, float alpha = 1f, int type = 1)
        {
            if (loaded) DrawShape.DrawFilledCircle(position, ResourceRadius, 1.5f, ResourceColorOf(type) * alpha, Color.Black * alpha);
            else DrawShape.DrawCircle(position, ResourceRadius, 1f, ResourceColor * (0.4f * alpha));
        }

        // One place per unit of capacity, behind the transporter along the way it came.
        private List<Vector2> WagonPositions(Transporter t)
        {
            var path = BackPath(t);
            return Enumerable.Range(0, t.Capacity).Select(i => PointAlong(path, FirstWagonOffset + i * WagonStep)).ToList();
        }

        // The way the transporter came, from where it is now back past its last wagon, with the distance walked to each point.
        // Past the start of its trail it continues in a straight line.
        private List<(Vector2 Point, float Along)> BackPath(Transporter t)
        {
            var path = new List<(Vector2 Point, float Along)> { (t.position, 0) };
            float walked = 0;
            Vector2 previous = t.position;
            Vector2 back = Vector2.Zero;

            for (int i = t.Trail.Count - 1; i >= 0; i--)
            {
                Vector2 p = PositionOn(t.Trail[i].Segment, t.Trail[i].Distance);
                float length = Vector2.Distance(previous, p);
                if (length <= 0) continue;
                back = (p - previous) / length;
                walked += length;
                path.Add((p, walked));
                previous = p;
            }

            if (back == Vector2.Zero)
            {
                Vector2 a = t.Segment.A.position, b = t.Segment.B.position;
                back = a == b ? -Vector2.UnitX : Vector2.Normalize(a - b);
            }
            float end = FirstWagonOffset + t.Capacity * WagonStep;
            if (walked < end) path.Add((previous + back * (end - walked), end));
            return path;
        }

        private static Vector2 PointAlong(List<(Vector2 Point, float Along)> path, float along)
        {
            for (int i = 1; i < path.Count; i++)
            {
                if (path[i].Along >= along)
                    return Vector2.Lerp(path[i - 1].Point, path[i].Point, (along - path[i - 1].Along) / (path[i].Along - path[i - 1].Along));
            }
            return path[^1].Point;
        }

        // small chevron in the middle of a one-way line showing its direction
        private static void DrawDirection(StyledSegment segment)
        {
            Vector2 a = segment.A.position, b = segment.B.position;
            if (Vector2.Distance(a, b) < 40f) return;

            Vector2 direction = Vector2.Normalize(b - a);
            Vector2 normal = VectorHelpers.Ortogonal(direction);
            Vector2 tip = VectorHelpers.Midpoint(a, b) + direction * (4f + segment.Width / 2f) + normal * (segment.Width / 2f + 6f);
            Color color = Color.White * 0.35f;
            DrawShape.DrawLine(tip, tip - direction * 5f + normal * 4f, color, 1.5f);
            DrawShape.DrawLine(tip, tip - direction * 5f - normal * 4f, color, 1.5f);
        }

        // digits, '/' and '.' on a dark box centered on a point; a space leaves a gap
        public static void DrawLabel(Vector2 center, params (string Text, Color Color)[] parts)
        {
            const float height = 10f, width = 5f, gap = 3f;
            float textWidth = parts.Sum(p => p.Text.Length) * (width + gap) - gap;
            Vector2 cursor = center - new Vector2(textWidth / 2f, height / 2f);

            DrawShape.DrawFilledRectangle(new Rectangle((int)(cursor.X - 4), (int)(cursor.Y - 4), (int)(textWidth + 8), (int)(height + 8)), Color.Black * 0.6f);

            foreach (var (text, color) in parts)
            {
                foreach (char c in text)
                {
                    DrawGlyph(c, cursor, width, height, color);
                    cursor.X += width + gap;
                }
            }
        }

        // a resource dot of the given type with its number on it, readable on light and dark colors
        public static void DrawResourceIcon(Vector2 center, float radius, int type)
        {
            const float height = 10f, width = 5f, gap = 3f;
            Color color = ResourceRecipes.ColorOf(type);
            DrawShape.DrawFilledCircle(center, radius, 1.5f, color, Color.Black);

            bool dark = color.R * 0.299f + color.G * 0.587f + color.B * 0.114f < 120f;
            string text = type.ToString();
            Vector2 cursor = center - new Vector2((text.Length * (width + gap) - gap) / 2f, height / 2f);
            foreach (char c in text)
            {
                DrawGlyph(c, cursor, width, height, dark ? Color.White : Color.Black);
                cursor.X += width + gap;
            }
        }

        // seven-segment glyphs: bits a (top), b (top right), c (bottom right), d (bottom), e (bottom left), f (top left), g (middle)
        private static readonly int[] DigitSegments = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F };

        private static void DrawGlyph(char c, Vector2 topLeft, float width, float height, Color color)
        {
            const float stroke = 1.5f;
            Vector2 tl = topLeft, tr = tl + new Vector2(width, 0);
            Vector2 ml = tl + new Vector2(0, height / 2f), mr = ml + new Vector2(width, 0);
            Vector2 bl = tl + new Vector2(0, height), br = bl + new Vector2(width, 0);

            if (c == '/') { DrawShape.DrawLine(bl, tr, color, stroke); return; }
            if (c == '.') { DrawShape.DrawFilledCircle(bl + new Vector2(width / 2f, -1f), 1.5f, 0f, color, Color.Transparent); return; }
            if (c < '0' || c > '9') return;

            int bits = DigitSegments[c - '0'];
            (Vector2, Vector2)[] lines = { (tl, tr), (tr, mr), (mr, br), (bl, br), (ml, bl), (tl, ml), (ml, mr) };
            for (int i = 0; i < lines.Length; i++)
                if ((bits & (1 << i)) != 0) DrawShape.DrawLine(lines[i].Item1, lines[i].Item2, color, stroke);
        }

        // ring that fills up clockwise until the next spawn / consume
        private static void DrawTimer(Vector2 center, float progress, Color color)
        {
            const float radius = 11f;
            if (progress >= 0.999f)
            {
                DrawShape.DrawCircle(center, radius, 2f, color);
                return;
            }
            if (progress <= 0.01f) return;

            float start = -MathHelper.PiOver2;
            float end = start + progress * MathHelper.TwoPi;
            DrawShape.DrawArc(center, radius, center + VectorHelpers.FromAngle(start, radius), center + VectorHelpers.FromAngle(end, radius), 2f, color);
        }

        #endregion
    }
}
