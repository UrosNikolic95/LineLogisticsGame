using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using LineLogisticsGame.Screens;
using LineLogisticsGame.Simulation;
using LineLogisticsGame.World;

namespace LineLogisticsGame.Persistence
{
    // A game written to a JSON file in the saves folder: recipes, points, statistics, camera, lines (with their produce / consume
    // timers), depots, transporters (with their cargo, route, the job they are scheduled for and the way they came) and the resources
    // on lines. Resources being pulled onto a transporter are kept as its cargo, and resources rolling onto a line are kept at the spot
    // they were rolling to. Saves from before this kept less: what they lack starts fresh (empty transporters plan again).
    public class SaveGame
    {
        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LineLogisticsGame", "Saves");

        // route legs use infinity for "the B end" (see Leg)
        private static readonly JsonSerializerOptions options = new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        public DateTime SavedAt { get; set; }
        public long Points { get; set; }
        public float CameraX { get; set; }
        public float CameraY { get; set; }
        public float Zoom { get; set; } = 1f;
        public bool GridEnabled { get; set; }
        public bool SnapToLines { get; set; } = true;
        public List<int[]> Requires { get; set; } = new();    // [produced, consumed] pairs
        public List<float[]> LinePoints { get; set; } = new(); // [x, y]
        public List<LineData> Lines { get; set; } = new();
        public List<DepotData> Depots { get; set; } = new();
        public List<TransporterData> Transporters { get; set; } = new();
        public List<ResourceData> Resources { get; set; } = new();
        public Statistics Statistics { get; set; }      // null in saves from before statistics were kept

        public class LineData
        {
            public int A { get; set; }
            public int B { get; set; }
            public uint Color { get; set; }
            public float Width { get; set; }
            public int Group { get; set; }
            public LineKind Kind { get; set; }
            public int Type { get; set; }
            public float Timer { get; set; }    // seconds its produce / consume timer has run
        }

        public class DepotData
        {
            public int Line { get; set; }
            public float Distance { get; set; }
            public int Capacity { get; set; }
            public float Speed { get; set; }
            public float Interval { get; set; }
            public int MaxTransporters { get; set; }
            public float Waiting { get; set; }
        }

        // line indexes are -1 for none
        public class TransporterData
        {
            public int Line { get; set; }
            public float Distance { get; set; }
            public int Capacity { get; set; }
            public float Speed { get; set; }
            public int Home { get; set; } = -1;     // index in Depots, -1: none
            public int Load { get; set; }
            public int CargoType { get; set; } = 1;
            public int FirstLoaded { get; set; }
            public List<LegData> Route { get; set; } = new();
            public int Pickup { get; set; } = -1;
            public int PickupOrder { get; set; } = -1;  // place in the queue of the green line
            public int Drop { get; set; } = -1;
            public int DropOrder { get; set; } = -1;    // place in the queue of the red line
            public int DropReserved { get; set; }
            public int Stop { get; set; } = -1;
            public bool Returning { get; set; }
            public float Waiting { get; set; }
            public float CurrentSpeed { get; set; }
            public float StillTime { get; set; }
            public List<float[]> Trail { get; set; } = new();   // [line, distance], oldest first
        }

        public class LegData
        {
            public int Line { get; set; }
            public float From { get; set; }
            public float To { get; set; }
        }

        public class ResourceData
        {
            public int Line { get; set; }
            public float Distance { get; set; }
            public int Type { get; set; }
        }

        // A file name no save has yet, for a game that was just started.
        public static string NewPath()
        {
            Directory.CreateDirectory(Folder);
            string name = $"game_{DateTime.Now:yyyyMMdd_HHmmss}";
            string path = Path.Combine(Folder, name + ".json");
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(Folder, $"{name}_{i}.json");
            return path;
        }

        // every save, most recent first
        public static List<(string Path, SaveGame Save)> All()
        {
            if (!Directory.Exists(Folder)) return new List<(string, SaveGame)>();
            var saves = new List<(string Path, SaveGame Save)>();
            foreach (string path in Directory.GetFiles(Folder, "*.json"))
            {
                try { saves.Add((path, Read(path))); }
                catch (Exception) { }   // not a save (or damaged): left out
            }
            return saves.OrderByDescending(s => s.Save.SavedAt).ToList();
        }

        public static SaveGame Read(string path) =>
            JsonSerializer.Deserialize<SaveGame>(File.ReadAllText(path), options) ?? throw new InvalidDataException(path);

        public static void Write(string path, TransportSystem transport, GameScreen screen)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Capture(transport, screen), options));
            File.Move(temporary, path, true);   // a crash while writing never leaves half a save
        }

        private static SaveGame Capture(TransportSystem transport, GameScreen screen)
        {
            LineDrawing drawing = screen.Drawing;
            var save = new SaveGame
            {
                SavedAt = DateTime.Now,
                Points = transport.Points,
                CameraX = screen.Camera.Position.X,
                CameraY = screen.Camera.Position.Y,
                Zoom = screen.Camera.Zoom,
                GridEnabled = drawing.GridEnabled,
                SnapToLines = drawing.SnapToLines,
                Statistics = transport.Statistics
            };

            for (int produced = 1; produced <= ResourceRecipes.Count; produced++)
                foreach (int consumed in transport.Recipes.RequirementsOf(produced))
                    save.Requires.Add(new[] { produced, consumed });

            save.LinePoints = drawing.Points.Select(p => new[] { p.position.X, p.position.Y }).ToList();
            save.Lines = drawing.Segments.Select(s => new LineData
            {
                A = drawing.Points.IndexOf(s.A), B = drawing.Points.IndexOf(s.B), Color = s.Color.PackedValue,
                Width = s.Width, Group = s.Group, Kind = s.Kind, Type = s.Type, Timer = transport.TimerOf(s)
            }).ToList();

            save.Depots = transport.Depots.Select(d => new DepotData
            {
                Line = drawing.Segments.IndexOf(d.Segment), Distance = d.Distance, Capacity = d.Capacity,
                Speed = d.Speed, Interval = d.Interval, MaxTransporters = d.MaxTransporters, Waiting = d.Waiting
            }).Where(d => d.Line >= 0).ToList();

            int Index(StyledSegment segment) => segment == null ? -1 : drawing.Segments.IndexOf(segment);
            save.Transporters = transport.Transporters.Select(t =>
            {
                var data = new TransporterData
                {
                    Line = Index(t.Segment), Distance = t.Distance, Capacity = t.Capacity,
                    Speed = t.speed, Home = t.Home == null ? -1 : transport.Depots.IndexOf(t.Home),
                    // what it is pulling off a green line already counts in Load: it comes back with them on its wagons
                    Load = t.Load, CargoType = t.CargoType, FirstLoaded = t.FirstLoaded,
                    Waiting = t.Waiting, CurrentSpeed = t.CurrentSpeed, StillTime = t.StillTime,
                    Trail = t.Trail.Where(p => Index(p.Segment) >= 0).Select(p => new[] { Index(p.Segment), p.Distance }).ToList()
                };

                // its plan, unless something in it is no longer drawn (then it plans again after loading)
                bool planValid = t.Route.All(leg => Index(leg.Segment) >= 0)
                                 && (t.PickupSegment == null || Index(t.PickupSegment) >= 0)
                                 && (t.DropSegment == null || Index(t.DropSegment) >= 0)
                                 && (t.Stop == null || Index(t.Stop) >= 0);
                if (planValid)
                {
                    data.Route = t.Route.Select(leg => new LegData { Line = Index(leg.Segment), From = leg.From, To = leg.To }).ToList();
                    data.Pickup = Index(t.PickupSegment);
                    data.PickupOrder = transport.PickupOrderOf(t);
                    data.Drop = Index(t.DropSegment);
                    data.DropOrder = transport.DropOrderOf(t);
                    data.DropReserved = t.DropReserved;
                    data.Stop = Index(t.Stop);
                    data.Returning = t.Returning;
                }
                return data;
            }).Where(t => t.Line >= 0).ToList();

            // Resources being pulled onto a transporter are in its Load. Ones rolling onto a line are kept at the spot they roll to.
            var towed = new HashSet<Resource>(transport.Transporters.SelectMany(t => t.Towing).Select(x => x.Resource));
            save.Resources = transport.Resources
                .Where(r => !towed.Contains(r))
                .Select(r => new ResourceData { Line = Index(r.Segment), Distance = r.Distance, Type = r.Type })
                .Where(r => r.Line >= 0).ToList();

            return save;
        }

        // Puts the saved game into a freshly made transport system and game screen.
        public void Apply(TransportSystem transport, GameScreen screen)
        {
            transport.Points = Points;
            if (Statistics != null)
            {
                if (!Statistics.IsValid) throw new InvalidDataException("statistics");
                transport.Statistics = Statistics;
            }
            transport.Recipes.Clear();
            foreach (int[] pair in Requires) transport.Recipes.Set(pair[0], pair[1], true);

            LineDrawing drawing = screen.Drawing;
            drawing.GridEnabled = GridEnabled;
            drawing.SnapToLines = SnapToLines;
            drawing.Load(
                LinePoints.Select(p => new Vector2(p[0], p[1])).ToArray(),
                Lines.Select(l => (l.A, l.B, new Color(l.Color), l.Width, l.Group, l.Kind, l.Type)).ToArray());

            screen.Camera.Position = new Vector2(CameraX, CameraY);
            screen.Camera.Zoom = Math.Clamp(Zoom, Camera.MinZoom, Camera.MaxZoom);

            for (int i = 0; i < Lines.Count; i++)
                if (Lines[i].Timer > 0) transport.SetTimer(drawing.Segments[i], Lines[i].Timer);

            var depots = Depots.Select(d =>
            {
                StyledSegment segment = drawing.Segments[d.Line];
                Depot depot = transport.AddDepot(segment, TransportSystem.PositionOn(segment, d.Distance), d.Capacity, d.Speed, d.Interval, d.MaxTransporters);
                depot.Waiting = d.Waiting;
                return depot;
            }).ToList();

            StyledSegment Line(int index) => index < 0 ? null : drawing.Segments[index];
            var loaded = new List<(Transporter Transporter, TransporterData Data)>();
            foreach (var t in Transporters)
            {
                StyledSegment segment = drawing.Segments[t.Line];
                Transporter transporter = transport.AddTransporter(segment, TransportSystem.PositionOn(segment, t.Distance), t.Capacity, t.Speed);
                transporter.Home = t.Home >= 0 && t.Home < depots.Count ? depots[t.Home] : null;

                if (t.CargoType < 1 || t.CargoType > ResourceRecipes.Count || t.Load < 0 || t.FirstLoaded < 0 || t.FirstLoaded + t.Load > t.Capacity)
                    throw new InvalidDataException("transporter cargo");
                transporter.Load = t.Load;
                transporter.CargoType = t.CargoType;
                transporter.FirstLoaded = t.FirstLoaded;
                transporter.Waiting = t.Waiting;
                transporter.CurrentSpeed = t.CurrentSpeed;
                transporter.StillTime = t.StillTime;
                transporter.LastPosition = transporter.position;
                transporter.Trail = t.Trail.Select(p => (Line((int)p[0]), p[1])).ToList();
                transporter.Route = t.Route.Select(leg => new Leg { Segment = Line(leg.Line), From = leg.From, To = leg.To }).ToList();
                transporter.PickupSegment = Line(t.Pickup);
                transporter.DropSegment = Line(t.Drop);
                transporter.DropReserved = t.DropReserved;
                transporter.Stop = Line(t.Stop);
                transporter.Returning = t.Returning;
                if (transporter.Trail.Any(p => p.Segment == null) || transporter.Route.Any(leg => leg.Segment == null))
                    throw new InvalidDataException("transporter route");
                loaded.Add((transporter, t));
            }

            // back in the queues of the lines they are scheduled on, in the order they were
            foreach (var (transporter, _) in loaded.Where(x => x.Transporter.PickupSegment != null).OrderBy(x => x.Data.PickupOrder))
                transport.ScheduleOnPickup(transporter);
            foreach (var (transporter, _) in loaded.Where(x => x.Transporter.DropSegment != null).OrderBy(x => x.Data.DropOrder))
                transport.ScheduleOnDrop(transporter);

            foreach (var r in Resources)
            {
                StyledSegment segment = drawing.Segments[r.Line];
                transport.Resources.Add(new Resource { Type = r.Type, Segment = segment, Distance = r.Distance, Position = TransportSystem.PositionOn(segment, r.Distance) });
            }
        }
    }
}
