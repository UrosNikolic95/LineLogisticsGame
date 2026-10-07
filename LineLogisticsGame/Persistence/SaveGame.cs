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
    // A game written to a JSON file in the saves folder: recipes, points, camera, lines, depots, transporters
    // and the resources lying on lines. What is in motion (routes, cargo, resources being towed or rolling in) is not kept:
    // transporters come back empty and plan again.
    public class SaveGame
    {
        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LineLogisticsGame", "Saves");

        private static readonly JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };

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

        public class LineData
        {
            public int A { get; set; }
            public int B { get; set; }
            public uint Color { get; set; }
            public float Width { get; set; }
            public int Group { get; set; }
            public LineKind Kind { get; set; }
            public int Type { get; set; }
        }

        public class DepotData
        {
            public int Line { get; set; }
            public float Distance { get; set; }
            public int Capacity { get; set; }
            public float Speed { get; set; }
            public float Interval { get; set; }
            public int MaxTransporters { get; set; }
        }

        public class TransporterData
        {
            public int Line { get; set; }
            public float Distance { get; set; }
            public int Capacity { get; set; }
            public float Speed { get; set; }
            public int Home { get; set; } = -1;     // index in Depots, -1: none
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
                SnapToLines = drawing.SnapToLines
            };

            for (int produced = 1; produced <= ResourceRecipes.Count; produced++)
                foreach (int consumed in transport.Recipes.RequirementsOf(produced))
                    save.Requires.Add(new[] { produced, consumed });

            save.LinePoints = drawing.Points.Select(p => new[] { p.position.X, p.position.Y }).ToList();
            save.Lines = drawing.Segments.Select(s => new LineData
            {
                A = drawing.Points.IndexOf(s.A), B = drawing.Points.IndexOf(s.B), Color = s.Color.PackedValue,
                Width = s.Width, Group = s.Group, Kind = s.Kind, Type = s.Type
            }).ToList();

            save.Depots = transport.Depots.Select(d => new DepotData
            {
                Line = drawing.Segments.IndexOf(d.Segment), Distance = d.Distance, Capacity = d.Capacity,
                Speed = d.Speed, Interval = d.Interval, MaxTransporters = d.MaxTransporters
            }).Where(d => d.Line >= 0).ToList();

            save.Transporters = transport.Transporters.Select(t => new TransporterData
            {
                Line = drawing.Segments.IndexOf(t.Segment), Distance = t.Distance, Capacity = t.Capacity,
                Speed = t.speed, Home = t.Home == null ? -1 : transport.Depots.IndexOf(t.Home)
            }).Where(t => t.Line >= 0).ToList();

            // only resources lying still on a line; ones being towed or rolling in are lost
            var towed = new HashSet<Resource>(transport.Transporters.SelectMany(t => t.Towing).Select(x => x.Resource));
            save.Resources = transport.Resources
                .Where(r => r.Path == null && !towed.Contains(r))
                .Select(r => new ResourceData { Line = drawing.Segments.IndexOf(r.Segment), Distance = r.Distance, Type = r.Type })
                .Where(r => r.Line >= 0).ToList();

            return save;
        }

        // Puts the saved game into a freshly made transport system and game screen.
        public void Apply(TransportSystem transport, GameScreen screen)
        {
            transport.Points = Points;
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

            var depots = Depots.Select(d =>
            {
                StyledSegment segment = drawing.Segments[d.Line];
                return transport.AddDepot(segment, TransportSystem.PositionOn(segment, d.Distance), d.Capacity, d.Speed, d.Interval, d.MaxTransporters);
            }).ToList();

            foreach (var t in Transporters)
            {
                StyledSegment segment = drawing.Segments[t.Line];
                Transporter transporter = transport.AddTransporter(segment, TransportSystem.PositionOn(segment, t.Distance), t.Capacity, t.Speed);
                transporter.Home = t.Home >= 0 && t.Home < depots.Count ? depots[t.Home] : null;
            }

            foreach (var r in Resources)
            {
                StyledSegment segment = drawing.Segments[r.Line];
                transport.Resources.Add(new Resource { Type = r.Type, Segment = segment, Distance = r.Distance, Position = TransportSystem.PositionOn(segment, r.Distance) });
            }
        }
    }
}
