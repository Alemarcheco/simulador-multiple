using System.Text.Json;
using System.Text.Json.Serialization;
using Simulador.Core.Building;

namespace Simulador.Civil.Model;

/// <summary>Nombre y uso que el usuario da a una habitación; se asocia al recinto que contiene el punto ancla.</summary>
public sealed record SpaceTag(string LevelId, Point2 Anchor, string Name, string Use);

/// <summary>Tipo de edificación (sirve para plantillas y, más adelante, para las reglas del asistente).</summary>
public enum BuildingType { Vivienda, Hotel, Industria, Comercio, Otro }

/// <summary>
/// Documento editable del módulo Edificación. Implementa <see cref="IBuildingModel"/> para que los demás módulos
/// coloquen sus elementos encima. Todas las modificaciones pasan por <see cref="Edit"/> para tener deshacer.
/// </summary>
public sealed class BuildingDocument : IBuildingModel
{
    private readonly List<Level> _levels = [];
    private readonly List<Wall> _walls = [];
    private readonly List<Opening> _openings = [];
    private readonly List<Slab> _slabs = [];
    private readonly List<Stair> _stairs = [];
    private readonly List<SpaceTag> _tags = [];
    private readonly List<PlacedFixture> _fixtures = [];
    private readonly List<Route> _routes = [];
    private List<Space> _spaces = [];
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();

    public string Name { get; set; } = "Edificio sin nombre";
    public BuildingType Type { get; set; } = BuildingType.Vivienda;

    /// <summary>Reglas del asistente y precios unitarios; se guardan con el proyecto.</summary>
    public AdvisorSettings Advisor { get; set; } = new();

    public IReadOnlyList<Level> Levels => _levels;
    public IReadOnlyList<Wall> Walls => _walls;
    public IReadOnlyList<Opening> Openings => _openings;
    public IReadOnlyList<Slab> Slabs => _slabs;
    public IReadOnlyList<Stair> Stairs => _stairs;
    public IReadOnlyList<SpaceTag> SpaceTags => _tags;
    public IReadOnlyList<Space> Spaces => _spaces;
    public IReadOnlyList<PlacedFixture> Fixtures => _fixtures;
    public IReadOnlyList<Route> Routes => _routes;

    public event EventHandler? Changed;

    public BuildingDocument()
    {
        _levels.Add(new Level("N0", "Planta baja", 0, 3.0));
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    // ---------------------------------------------------------------- edición

    /// <summary>Aplica un cambio guardando el estado anterior para poder deshacerlo.</summary>
    public void Edit(Action<Editor> change)
    {
        _undo.Push(ToJson());
        _redo.Clear();
        change(new Editor(this));
        OnChanged();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(ToJson());
        LoadJson(_undo.Pop());
        OnChanged();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(ToJson());
        LoadJson(_redo.Pop());
        OnChanged();
    }

    private void OnChanged()
    {
        RecomputeSpaces();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Operaciones de edición. Solo se obtiene dentro de <see cref="Edit"/>.</summary>
    public sealed class Editor
    {
        private readonly BuildingDocument _d;
        internal Editor(BuildingDocument d) => _d = d;

        public string NewId(string prefix)
        {
            var used = _d._walls.Select(w => w.Id).Concat(_d._openings.Select(o => o.Id)).Concat(_d._slabs.Select(s => s.Id))
                .Concat(_d._stairs.Select(s => s.Id)).Concat(_d._fixtures.Select(f => f.Id)).Concat(_d._routes.Select(r => r.Id))
                .Concat(_d._levels.Select(l => l.Id)).ToHashSet();
            for (int i = 1; ; i++)
                if (!used.Contains(prefix + i)) return prefix + i;
        }

        /// <summary>Siguiente designación libre de componente (X1, X2…) para un prefijo dado.</summary>
        public string NewComponentId(string prefix)
        {
            var used = _d._fixtures.Select(f => f.ComponentId).ToHashSet();
            for (int i = 1; ; i++)
                if (!used.Contains(prefix + i)) return prefix + i;
        }

        public void Clear()
        {
            _d._levels.Clear(); _d._walls.Clear(); _d._openings.Clear(); _d._slabs.Clear();
            _d._stairs.Clear(); _d._tags.Clear(); _d._fixtures.Clear(); _d._routes.Clear();
        }

        public Level AddLevel(string name, double elevation, double height, string? id = null)
        {
            var l = new Level(id ?? NewId("N"), name, elevation, height);
            _d._levels.Add(l);
            return l;
        }

        public Wall AddWall(string levelId, Point2 a, Point2 b, double thickness = 0.15, double height = 2.8, string material = "bloque")
        {
            var w = new Wall(NewId("M"), levelId, a, b, thickness, height, material);
            _d._walls.Add(w);
            return w;
        }

        /// <summary>Muros cerrados a lo largo de un polígono.</summary>
        public IReadOnlyList<Wall> AddWallLoop(string levelId, IReadOnlyList<Point2> pts, double thickness = 0.15, double height = 2.8)
        {
            var walls = new List<Wall>();
            for (int i = 0; i < pts.Count; i++) walls.Add(AddWall(levelId, pts[i], pts[(i + 1) % pts.Count], thickness, height));
            return walls;
        }

        public Opening AddDoor(Wall wall, double offset, double width = 0.8, double height = 2.1)
        {
            var o = new Opening(NewId("P"), wall.Id, OpeningKind.Door, offset, width, height, 0);
            _d._openings.Add(o);
            return o;
        }

        public Opening AddWindow(Wall wall, double offset, double width = 1.2, double height = 1.2, double sill = 0.9)
        {
            var o = new Opening(NewId("V"), wall.Id, OpeningKind.Window, offset, width, height, sill);
            _d._openings.Add(o);
            return o;
        }

        public Slab AddSlab(string levelId, IReadOnlyList<Point2> outline, double thickness = 0.15, SlabKind kind = SlabKind.Floor)
        {
            var s = new Slab(NewId("L"), levelId, outline, thickness, kind);
            _d._slabs.Add(s);
            return s;
        }

        public Stair AddStair(string levelId, Point2 start, double angle, double width, double rise,
            StairShape shape = StairShape.Straight, bool turnLeft = true)
        {
            var s = new Stair(NewId("E"), levelId, start, angle, width, rise, shape, turnLeft);
            _d._stairs.Add(s);
            return s;
        }

        public void TagSpace(string levelId, Point2 anchor, string name, string use)
        {
            _d._tags.RemoveAll(t => t.LevelId == levelId && t.Anchor.DistanceTo(anchor) < 1e-6);
            _d._tags.Add(new SpaceTag(levelId, anchor, name, use));
        }

        /// <summary>Renombra el recinto que contiene <paramref name="point"/> (sustituye la etiqueta que hubiera dentro).</summary>
        public void RenameSpaceAt(string levelId, Point2 point, string name, string use)
        {
            var space = _d._spaces.FirstOrDefault(s => s.LevelId == levelId && Geo.Contains(s.Outline, point));
            if (space is not null)
                _d._tags.RemoveAll(t => t.LevelId == levelId && Geo.Contains(space.Outline, t.Anchor));
            _d._tags.Add(new SpaceTag(levelId, point, name, use));
        }

        public PlacedFixture AddFixture(PlacedFixture f)
        {
            _d._fixtures.Add(f);
            return f;
        }

        public Route AddRoute(Route r)
        {
            _d._routes.Add(r);
            return r;
        }

        public void ReplaceLevel(string levelId, Func<Level, Level> change)
        {
            int i = _d._levels.FindIndex(l => l.Id == levelId);
            _d._levels[i] = change(_d._levels[i]) with { Id = levelId };
        }

        /// <summary>Quita un nivel (debe estar vacío; borra antes su contenido). Siempre queda al menos uno.</summary>
        public void RemoveLevel(string levelId)
        {
            if (_d._levels.Count <= 1) return;
            _d._levels.RemoveAll(l => l.Id == levelId);
            _d._tags.RemoveAll(t => t.LevelId == levelId);
        }

        public void Replace(Wall old, Wall @new) => _d._walls[_d._walls.IndexOf(old)] = @new;
        public void Replace(Opening old, Opening @new) => _d._openings[_d._openings.IndexOf(old)] = @new;
        public void Replace(Stair old, Stair @new) => _d._stairs[_d._stairs.IndexOf(old)] = @new;

        /// <summary>Borra un elemento por id junto con lo que depende de él (huecos de un muro, rutas de un elemento).</summary>
        public void Delete(string id)
        {
            if (_d._walls.RemoveAll(w => w.Id == id) > 0)
            {
                _d._openings.RemoveAll(o => o.HostWallId == id);
                _d._fixtures.RemoveAll(f => f.HostId == id);
            }
            _d._openings.RemoveAll(o => o.Id == id);
            _d._slabs.RemoveAll(s => s.Id == id);
            _d._stairs.RemoveAll(s => s.Id == id);
            _d._fixtures.RemoveAll(f => f.Id == id);
            var fixtureIds = _d._fixtures.Select(f => f.Id).ToHashSet();
            _d._routes.RemoveAll(r => r.Id == id || !fixtureIds.Contains(r.FromFixtureId) || !fixtureIds.Contains(r.ToFixtureId));
        }

        /// <summary>
        /// Copia todo lo de un nivel a un nivel nuevo encima (planta tipo de hotel o edificio de viviendas).
        /// Las escaleras no se copian en el último nivel creado si no hay nivel superior; se copian igualmente para editarlas.
        /// </summary>
        public Level DuplicateLevel(string levelId, string? name = null)
        {
            var src = _d._levels.First(l => l.Id == levelId);
            var top = _d._levels.MaxBy(l => l.Elevation)!;
            var lvl = AddLevel(name ?? $"Planta {_d._levels.Count}", top.Elevation + top.Height, src.Height);

            var wallMap = new Dictionary<string, string>();
            foreach (var w in _d._walls.Where(w => w.LevelId == levelId).ToList())
            {
                var nw = w with { Id = NewId("M"), LevelId = lvl.Id };
                _d._walls.Add(nw);
                wallMap[w.Id] = nw.Id;
            }
            foreach (var o in _d._openings.Where(o => wallMap.ContainsKey(o.HostWallId)).ToList())
                _d._openings.Add(o with { Id = NewId(o.Kind == OpeningKind.Door ? "P" : "V"), HostWallId = wallMap[o.HostWallId] });
            foreach (var s in _d._slabs.Where(s => s.LevelId == levelId).ToList())
                _d._slabs.Add(s with { Id = NewId("L"), LevelId = lvl.Id });
            foreach (var s in _d._stairs.Where(s => s.LevelId == levelId).ToList())
                _d._stairs.Add(s with { Id = NewId("E"), LevelId = lvl.Id });
            foreach (var t in _d._tags.Where(t => t.LevelId == levelId).ToList())
                _d._tags.Add(t with { LevelId = lvl.Id });
            return lvl;
        }
    }

    // ---------------------------------------------------------------- consultas

    public Level GetLevel(string id) => _levels.First(l => l.Id == id);

    public Point3 GetWallPoint(string wallId, WallFace face, double distance, double height)
    {
        var w = _walls.First(x => x.Id == wallId);
        var level = GetLevel(w.LevelId);
        var dir = w.Direction;
        var normal = dir.Perp() * (face == WallFace.A ? 1 : -1);
        var p = w.Start + dir * distance + normal * (w.Thickness / 2);
        return new Point3(p, level.Elevation + height);
    }

    /// <summary>Muro del nivel más cercano a un punto (y la distancia al eje), o null si está más lejos que <paramref name="maxDistance"/>.</summary>
    public (Wall Wall, double Along, WallFace Face)? NearestWall(string levelId, Point2 p, double maxDistance)
    {
        Wall? best = null;
        double bestD = maxDistance;
        foreach (var w in _walls.Where(w => w.LevelId == levelId))
        {
            double d = Geo.DistanceToSegment(w.Start, w.End, p);
            if (d <= bestD) { bestD = d; best = w; }
        }
        if (best is null) return null;
        double along = Geo.ProjectT(best.Start, best.End, p) * best.Length;
        var face = Point2.Cross(best.End - best.Start, p - best.Start) >= 0 ? WallFace.A : WallFace.B;
        return (best, along, face);
    }

    public Space? SpaceAt(string levelId, Point2 p) =>
        _spaces.Where(s => s.LevelId == levelId && Geo.Contains(s.Outline, p)).MinBy(s => s.Area);

    private void RecomputeSpaces()
    {
        var spaces = new List<Space>();
        foreach (var level in _levels)
        {
            var rooms = SpaceDetector.FindRooms(_walls.Where(w => w.LevelId == level.Id).Select(w => (w.Start, w.End)));
            int n = 1;
            foreach (var poly in rooms.OrderBy(p => Geo.Centroid(p).Y).ThenBy(p => Geo.Centroid(p).X))
            {
                var tag = _tags.FirstOrDefault(t => t.LevelId == level.Id && Geo.Contains(poly, t.Anchor));
                spaces.Add(new Space(
                    $"{level.Id}-R{n}", level.Id,
                    tag?.Name ?? $"Espacio {n}", tag?.Use ?? "sin_uso",
                    poly, Geo.SignedArea(poly), Geo.Perimeter(poly)));
                n++;
            }
        }
        _spaces = spaces;
    }

    // ---------------------------------------------------------------- archivo

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record FileDto(
        int Version, string Name, BuildingType Type,
        List<Level> Levels, List<Wall> Walls, List<Opening> Openings, List<Slab> Slabs, List<Stair> Stairs,
        List<SpaceTag> SpaceTags, List<PlacedFixture> Fixtures, List<Route> Routes, AdvisorSettings? Advisor = null);

    public const int FileVersion = 1;
    public const string FileExtension = ".edif.json";

    public string ToJson() => JsonSerializer.Serialize(
        new FileDto(FileVersion, Name, Type, _levels, _walls, _openings, _slabs, _stairs, _tags, _fixtures, _routes, Advisor), JsonOpts);

    public static BuildingDocument FromJson(string json)
    {
        var d = new BuildingDocument();
        d.LoadJson(json);
        d.RecomputeSpaces();
        return d;
    }

    private void LoadJson(string json)
    {
        var dto = JsonSerializer.Deserialize<FileDto>(json, JsonOpts) ?? throw new InvalidDataException("Archivo de edificio vacío.");
        if (dto.Version > FileVersion) throw new InvalidDataException($"El archivo es de una versión más nueva ({dto.Version}).");
        Name = dto.Name;
        Type = dto.Type;
        Replace(_levels, dto.Levels); Replace(_walls, dto.Walls); Replace(_openings, dto.Openings);
        Replace(_slabs, dto.Slabs); Replace(_stairs, dto.Stairs); Replace(_tags, dto.SpaceTags);
        Replace(_fixtures, dto.Fixtures); Replace(_routes, dto.Routes);
        Advisor = dto.Advisor ?? new AdvisorSettings();
        if (_levels.Count == 0) _levels.Add(new Level("N0", "Planta baja", 0, 3.0));
    }

    private static void Replace<T>(List<T> target, List<T>? src)
    {
        target.Clear();
        if (src is not null) target.AddRange(src);
    }

    /// <summary>Reemplaza todo el contenido (p. ej. al abrir un archivo o cargar una plantilla) vaciando el historial.</summary>
    public void LoadFrom(BuildingDocument other)
    {
        LoadJson(other.ToJson());
        _undo.Clear();
        _redo.Clear();
        OnChanged();
    }
}
