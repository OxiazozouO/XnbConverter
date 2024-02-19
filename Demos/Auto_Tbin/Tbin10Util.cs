using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using XnbConverter.Entity.Mono;
using XnbConverter.Tbin.Entity;

namespace Auto_Tbin;

public static class Tbin10Util
{
    private const int Val = 2000;
    private static readonly int[] Values;

    static Tbin10Util()
    {
        var values = new List<int>();
        for (var i = 1; i <= Val; i++)
            if (Val % i == 0)
                values.Add(i);

        Values = values.ToArray();
    }

    private static void LoadImages(this List<TileSheet> tileSheets, string path,
        out Dictionary<string, List<Image<Rgba32>>> imgList, out HashSet<string> seasonSet)
    {
        imgList = new Dictionary<string, List<Image<Rgba32>>>();
        seasonSet = new HashSet<string>();
        string ex = "";
        foreach (var v in tileSheets)
        {
            if (v.Image.Contains("spring_"))
            {
                List<Image<Rgba32>> lt = new List<Image<Rgba32>>();
                foreach (var season in GetSeasons(v.Image))
                {
                    var s = Path.Combine(path, season + ".png");

                    if (File.Exists(s))
                    {
                        var image = Image.Load<Rgba32>(s);
                        lt.Add(image);
                    }
                    else
                    {
                        ex += s + "\n";
                    }
                }

                imgList.Add(v.Id, lt);
                seasonSet.Add(v.Id);
            }
            else
            {
                var s = Path.Combine(path, v.Image + ".png");
                if (File.Exists(s))
                {
                    if (!imgList.ContainsKey(v.Id))
                    {
                        var image = Image.Load<Rgba32>(s);
                        List<Image<Rgba32>> lt = new List<Image<Rgba32>>();
                        lt.Add(image);
                        imgList.Add(v.Id, lt);
                    }
                }
                else
                {
                    ex += s + "\n";
                }
            }
        }

        if (ex != "")
        {
            throw new Exception("下列文件不存在：" + ex);
        }
    }

    private static void ProcessMap(this List<Layer> layers, out Dictionary<string, List<Layer>> map)
    {
        map = new Dictionary<string, List<Layer>>();
        foreach (var la in layers)
        {
            string name = "";
            if (la.Properties.Count == 0)
            {
                name = la.Id;
            }
            else
            {
                foreach (var v in la.Properties.Where(v => v.Key == "Draw"))
                {
                    name = la.Properties[0].Value as string;
                }
            }

            if (name == "")
            {
                throw new ArgumentException("Bad tile data");
            }

            if (!map.ContainsKey(name))
            {
                map[name] = new List<Layer>();
            }

            map[name].Add(la);
        }

        {
            var tag = true;
            foreach (var v in map)
            {
                if (v.Key.Length == 1)
                {
                    continue;
                }

                tag = false;
            }

            if (tag)
            {
                return;
            }
        }
    }

    private static void ExtractProperties(this List<TileSheet> tileSheets,
        out Dictionary<string, List<Propertie>> properties)
    {
        properties = new Dictionary<string, List<Propertie>>();
        foreach (var sheet in tileSheets)
        {
            foreach (var prop in sheet.Properties)
            {
                var strId = sheet.Id + "@" + prop.Key.Split("@")[2];
                if (!properties.ContainsKey(strId))
                {
                    properties[strId] = new List<Propertie>();
                }

                properties[strId].Add(prop);
            }
        }
    }


    public static void ConsolidateLayers(this TBin10 tbin, string path)
    {
        tbin.Layers.ProcessMap(out var map);
        // foreach (var v in map)
        // {
        //     if (v.Value.Count == 1)
        //     {
        //         continue;
        //     }
        //
        //     goto run;
        // }
        //
        // return;
        run:
        var imgList = new ImgLists(path);
        var indexed = new Dictionary<string, (int, int)>();
        var layers = new List<Layer>();
        var sb = new StringBuilder();

        tbin.RemoveTileSheetsExtension();
        tbin.TileSheets.LoadImages(path, out var list, out var seasonSet);
        tbin.TileSheets.ExtractProperties(out var properties);

        foreach (var v in map)
        {
            if (v.Value.Count == 1)
            {
                layers.Add(v.Value[0]);
                continue;
            }

            var ll = v.Value;

            var layer = new Layer
            {
                Id = v.Key,
                Visible = ll[0].Visible,
                Description = "",
                LayerSize = ll[0].LayerSize,
                TileSize = ll[0].TileSize,
                Properties = new List<Propertie>(),
                Tiles = new List<BaseTile>()
            };
            layers.Add(layer);

            for (var j = 0; j < ll[0].Tiles.Count; ++j)
            {
                var staticTile = new StaticTile[ll.Count];
                var staInd = 0;
                var animatedTile = new AnimatedTile[ll.Count];
                var anmInd = 0;
                foreach (var t in ll)
                    switch (t.Tiles[j])
                    {
                        case null:
                            continue;
                        case StaticTile:
                            staticTile[staInd++] = t.Tiles[j] as StaticTile;
                            break;
                        case AnimatedTile:
                            animatedTile[anmInd++] = t.Tiles[j] as AnimatedTile;
                            break;
                        default:
                            throw new Exception("Bad tile data");
                    }

                switch (staInd)
                {
                    case 0 when anmInd == 0:
                        layer.Tiles.Add(null);
                        break;
                    case 1 when anmInd == 0:
                        layer.Tiles.Add(staticTile[0]);
                        break;
                    case 0 when anmInd == 1:
                        layer.Tiles.Add(animatedTile[0]);
                        break;
                    default:
                    {
                        if (staInd > 0 || anmInd > 0)
                        {
                            sb.Length = 0;
                            for (var i = 0; i < staInd; ++i)
                            {
                                sb.Append(staticTile[i].TileSheet).Append('_').Append(staticTile[i].TileIndex)
                                    .Append('_');
                            }

                            for (var i = 0; i < anmInd; ++i)
                            {
                                foreach (var an in animatedTile[i].Frames)
                                {
                                    sb.Append(an.TileSheet).Append('_').Append(an.TileIndex).Append('_');
                                }
                            }

                            if (indexed.TryGetValue(sb.ToString(), out var value))
                            {
                                layer.Tiles.Add(layers[value.Item1].Tiles[value.Item2]);
                            }
                            else
                            {
                                Image<Rgba32>[] image = null;
                                bool tag = false;
                                for (var i = 0; i < staInd; ++i)
                                {
                                    if (!seasonSet.Contains(staticTile[i].TileSheet)) continue;
                                    tag = true;
                                    break;
                                }

                                if (tag)
                                {
                                    image = new Image<Rgba32>[]
                                    {
                                        new Image<Rgba32>(16, 16),
                                        new Image<Rgba32>(16, 16),
                                        new Image<Rgba32>(16, 16),
                                        new Image<Rgba32>(16, 16),
                                    };
                                    for (var i = 0; i < staInd; ++i)
                                    {
                                        StaticTile st = staticTile[i];
                                        if (seasonSet.Contains(st.TileSheet))
                                        {
                                            for (int k = 0; k < 4; k++)
                                            {
                                                image[k].DrawImagePortion(0, list[st.TileSheet][k], st.TileIndex);
                                            }
                                        }
                                        else
                                        {
                                            for (int k = 0; k < 4; k++)
                                            {
                                                image[k].DrawImagePortion(0, list[st.TileSheet][0], st.TileIndex);
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    image = new Image<Rgba32>[]
                                    {
                                        new Image<Rgba32>(16, 16)
                                    };
                                    for (var i = 0; i < staInd; ++i)
                                    {
                                        image[0].DrawImagePortion(0, list[staticTile[i].TileSheet][0],
                                            staticTile[i].TileIndex);
                                    }
                                }

                                var pl = new List<Propertie>();
                                for (var i = 0; i < staInd; ++i)
                                {
                                    if (properties.TryGetValue(staticTile[i].GetId(), out var property))
                                    {
                                        pl.AddRange(property.Select(tmp => new Propertie
                                            { Key = tmp.Key, Type = tmp.Type, Value = tmp.Value }));
                                    }
                                }

                                if (anmInd == 0)
                                {
                                    imgList.Add(image);
                                    var all = new StaticTile
                                    {
                                        Properties = new List<Propertie>(),
                                        TileSheet = imgList.NowName(),
                                        TileIndex = imgList.NowIndex(),
                                        BlendMode = 0
                                    };
                                    if (pl.Count > 0)
                                    {
                                        foreach (var p in pl)
                                        {
                                            var ss = p.Key.Split("@");
                                            p.Key = "@" + ss[1] + "@" + imgList.NowIndex() + "@" + ss[3];
                                        }

                                        imgList.AddProperties(pl);
                                    }

                                    for (var i = 0; i < staInd; i++)
                                    {
                                        if (staticTile[i].Properties.Count < 1) //事件
                                        {
                                            continue;
                                        }

                                        all.Properties.AddRange(staticTile[i].Properties); //需要进一步处理
                                    }

                                    layer.Tiles.Add(all);
                                    indexed.Add(sb.ToString(), (layers.Count - 1, layer.Tiles.Count - 1));
                                }
                                else
                                {
                                    var gcd = animatedTile[0].FrameInterval;
                                    var lcm = 1;
                                    for (var i = 0; i < anmInd; ++i)
                                    {
                                        animatedTile[i].FrameInterval = FindNearestValue(animatedTile[i].FrameInterval);
                                        gcd = GCD(gcd, animatedTile[i].FrameInterval);
                                        lcm = LCM(lcm, animatedTile[i].FrameInterval * animatedTile[i].Frames.Count);
                                    }

                                    var arr = new int[anmInd + 1][];
                                    var ma = lcm / gcd;
                                    {
                                        arr[0] = new int[ma];
                                        for (var k = 0; k < ma; ++k)
                                        {
                                            arr[0][k] = k * gcd;
                                        }

                                        for (var i = 0; i < anmInd; i++)
                                        {
                                            arr[i + 1] = new int[ma];
                                            var ii = -1;
                                            for (var k = 0; k < ma; ++k)
                                            {
                                                if (k * gcd % animatedTile[i].FrameInterval == 0)
                                                {
                                                    ii = (ii + 1) % animatedTile[i].Frames.Count;
                                                }

                                                arr[i + 1][k] = ii;
                                            }
                                        }
                                    }

                                    var anTile = new AnimatedTile
                                    {
                                        Properties = new List<Propertie>(),
                                        FrameInterval = gcd,
                                        Frames = new List<StaticTile>(ma),
                                        _frameCount = ma
                                    };
                                    for (var i = 0; i < anmInd; ++i)
                                    {
                                        foreach (var frame in animatedTile[i].Frames)
                                        {
                                            if (!seasonSet.Contains(frame.TileSheet)) continue;
                                            tag = true;
                                            break;
                                        }
                                    }

                                    for (var i = 0; i < ma; i++)
                                    {
                                        Image<Rgba32>[] imgCopy;
                                        if (tag && image.Length == 1)
                                        {
                                            imgCopy = new Image<Rgba32>[4];
                                            for (int k = 0; k < 4; k++)
                                            {
                                                imgCopy[k] = image[0].Clone();
                                            }
                                        }
                                        else
                                        {
                                            imgCopy = new Image<Rgba32>[image.Length];
                                            for (int k = 0; k < image.Length; k++)
                                            {
                                                imgCopy[k] = image[k].Clone();
                                            }
                                        }

                                        if (tag)
                                        {
                                            for (var k = 0; k < anmInd; k++)
                                            {
                                                var frame = animatedTile[k].Frames[arr[k + 1][i]];
                                                for (var index = 0; index < 4; index++)
                                                {
                                                    imgCopy[index].DrawImagePortion(0, list[frame.TileSheet][index],
                                                        frame.TileIndex);
                                                }
                                            }
                                        }
                                        else
                                        {
                                            for (var k = 0; k < anmInd; k++)
                                            {
                                                var frame = animatedTile[k].Frames[arr[k + 1][i]];
                                                imgCopy[0].DrawImagePortion(0, list[frame.TileSheet][0],
                                                    frame.TileIndex);
                                            }
                                        }


                                        var pll = new List<Propertie>();
                                        pll.AddRange(pl.Select(tmp => new Propertie
                                            { Key = tmp.Key, Type = tmp.Type, Value = tmp.Value }));
                                        for (var k = 0; k < anmInd; k++)
                                        {
                                            var frame = animatedTile[k].Frames[arr[k + 1][i]];

                                            if (properties.TryGetValue(frame.GetId(), out var proper))
                                            {
                                                pll.AddRange(proper.Select(tmp => new Propertie
                                                    { Key = tmp.Key, Type = tmp.Type, Value = tmp.Value }));
                                            }
                                        }

                                        imgList.Add(imgCopy);
                                        var tmp = new StaticTile
                                        {
                                            Properties = new List<Propertie>(),
                                            TileSheet = imgList.NowName(),
                                            TileIndex = imgList.NowIndex(),
                                            BlendMode = 0
                                        };
                                        if (pll.Count > 0)
                                        {
                                            foreach (var p in pll)
                                            {
                                                var ss = p.Key.Split("@");
                                                p.Key = "@" + ss[1] + "@" + imgList.NowIndex() + "@" + ss[3];
                                            }

                                            imgList.AddProperties(pll);
                                        }

                                        anTile.Frames.Add(tmp);
                                    }

                                    (anTile.Index, _, anTile._currTileSheet) =
                                        anTile.Frames.CompileIndex(layer.LayerSize.X);

                                    layer.Tiles.Add(anTile);

                                    indexed.Add(sb.ToString(), (layers.Count - 1, layer.Tiles.Count - 1));
                                }
                            }
                        }
                        else
                        {
                            throw new NotImplementedException();
                        }

                        break;
                    }
                }
            }

            (layer.Index, layer._sizeArr, layer._currTileSheet) = layer.Tiles.CompileIndex(layer.LayerSize.X);
        }

        tbin.Layers = layers;
        imgList.Save();
        tbin.TileSheets.AddRange(imgList.TileSheets);
    }

    private static string[] GetSeasons(string s)
    {
        s = s.Replace("spring_", "");
        return new[] { "spring_" + s, "summer_" + s, "fall_" + s, "winter_" + s };
    }

    private static (List<char>, List<int>, List<string>) CompileIndex<T>(this List<T> tiles, int w)
    {
        List<char> index = new List<char>();
        List<int> sizeArr = new List<int>();
        List<string> currTileSheet = new List<string>();
        currTileSheet.Add("");

        for (var i = 0; i < tiles.Count; ++i)
        {
            if (tiles[i] is null)
            {
                int next = tiles.Count;
                for (int j = i + 1; j < tiles.Count; j++)
                {
                    if (tiles[j] == null) continue;
                    next = j; //j : not null
                    break;
                }

                var ww = SubLen(i, next, w); //i:null  j : not null
                foreach (var l in ww)
                {
                    index.Add('N');
                    sizeArr.Add(l);
                }

                i = next - 1; //-1:抵消++i
            }
            else if (tiles[i] is StaticTile)
            {
                var tile = tiles[i] as StaticTile;
                if (currTileSheet[^1] != tile.TileSheet)
                {
                    index.Add('T');
                    currTileSheet.Add(tile.TileSheet);
                }

                index.Add('S');
            }
            else if (tiles[i] is AnimatedTile)
            {
                index.Add('A');
            }
        }

        return (index, sizeArr, currTileSheet);
    }

    private static int[] SubLen(int i, int j, int w) //i:null  j : not null
    {
        int size = j - i;
        i %= w;
        int t = w - i;
        if (t >= size) return new[] { size };

        var list = new List<int>();
        list.Add(t);
        size -= t;
        var nn = size / w;
        for (var k = 0; k < nn; k++)
            list.Add(w);

        size -= w * nn;
        list.Add(size);
        return list.ToArray();
    }

    private static string GetImgName()
    {
        return new StringBuilder()
            .Append(DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"))
            .Append(Guid.NewGuid().ToString().Replace("-", ""))
            .ToString();
    }

    public static int GCD(int a, int b)
    {
        while (b != 0)
        {
            var temp = a % b;
            a = b;
            b = temp;
        }

        return a;
    }

    public static int LCM(int a, int b)
    {
        return a * b / GCD(a, b);
    }

    private static int FindNearestValue(int n)
    {
        if (n <= Values[0])
            return Values[0];
        if (n >= Values[^1])
            return Values[^1];

        int left = 0, right = Values.Length - 1;
        while (left < right)
        {
            var mid = left + (right - left) / 2;
            if (Values[mid] == n)
                return Values[mid];
            if (Values[mid] < n)
                left = mid + 1;
            else
                right = mid;
        }

        // 比较当前元素与其左侧元素的距离，确保找到最近的值
        return Math.Abs(n - Values[left]) > Math.Abs(n - Values[left - 1]) ? Values[left - 1] : Values[left];
    }

    private class ImgLists
    {
        private const int DefSize = 1250;
        private readonly string _path;

        public readonly List<TileSheet> TileSheets = new();
        private readonly Dictionary<int, List<List<Image<Rgba32>>>> _tmpList = new();
        private readonly Dictionary<int, TileSheet> _tmpTileSheet = new();
        private readonly Dictionary<int, List<string>> _pathNames = new();
        private int _w;

        public ImgLists(string path)
        {
            _path = path;
        }

        public void Add(params Image<Rgba32>[] img)
        {
            _w = img.Length;
            if (_tmpList.TryGetValue(_w, out var value))
            {
                if (value[0].Count == DefSize * DefSize)
                {
                    Save(value);
                    for (int i = 0; i < value.Count; i++)
                    {
                        value[i] = new List<Image<Rgba32>>();
                    }

                    _pathNames[_w] = new List<string>();
                    _tmpTileSheet[_w] = new TileSheet
                    {
                        Id = NowName(),
                        Description = "",
                        Image = NowName(),
                        SheetSize = null,
                        TileSize = new IntVector2 { X = 16, Y = 16 },
                        Margin = new IntVector2 { X = 0, Y = 0 },
                        Spacing = new IntVector2 { X = 0, Y = 0 },
                        Properties = new List<Propertie>()
                    };
                }
            }
            else
            {
                _tmpList.Add(_w, new List<List<Image<Rgba32>>>());
                for (int i = 0; i < _w; i++)
                {
                    _tmpList[_w].Add(new List<Image<Rgba32>>());
                }

                if (!_pathNames.ContainsKey(_w))
                {
                    _pathNames.Add(_w, new List<string>());
                }

                _pathNames[_w].Add(GetImgName());

                if (!_tmpTileSheet.ContainsKey(_w))
                {
                    var tmpT = new TileSheet
                    {
                        Id = NowName(),
                        Description = "",
                        Image = NowName(),
                        SheetSize = null,
                        TileSize = new IntVector2 { X = 16, Y = 16 },
                        Margin = new IntVector2 { X = 0, Y = 0 },
                        Spacing = new IntVector2 { X = 0, Y = 0 },
                        Properties = new List<Propertie>()
                    };
                    TileSheets.Add(tmpT);
                    _tmpTileSheet.Add(_w, tmpT);
                }
            }

            for (int i = 0; i < _w; i++)
            {
                _tmpList[_w][i].Add(img[i]);
            }
        }

        public void AddProperties(List<Propertie> pl)
        {
            _tmpTileSheet[_w].Properties.AddRange(pl);
        }

        public void Save(List<List<Image<Rgba32>>> list)
        {
            int num;
            if (list[0].Count < DefSize * DefSize)
            {
                num = (int)Math.Sqrt(list[0].Count);
                if (num * num < list[0].Count)
                {
                    ++num;
                }

                num *= 16;
            }
            else
            {
                num = DefSize * 16;
            }

            string[] s = { "" };
            if (list.Count == 4)
            {
                s = new string[] { "spring_", "summer_", "fall_", "winter_" };
            }

            for (int i = 0; i < list.Count; ++i)
            {
                var m = new Image<Rgba32>(num, num); //2
                _tmpTileSheet[_w].SheetSize = new IntVector2 { X = num / 16, Y = num / 16 };
                for (var j = 0; j < list[0].Count; ++j)
                {
                    m.DrawImagePortion(j, list[i][j], 0);
                }

                m.Save(Path.Combine(_path, s[i] + _pathNames[_w][^1] + ".png"));
            }
        }

        public void Save()
        {
            foreach (var (key, value) in _tmpList)
            {
                if (value[0].Count > 0)
                {
                    _w = key;
                    Save(value);
                }
            }
        }

        public string NowName()
        {
            if (_w == 4)
            {
                return "spring_" + _pathNames[_w][^1];
            }

            return _pathNames[_w][^1];
        }

        public int NowIndex()
        {
            return _tmpList[_w][0].Count - 1;
        }
    }


    public static object[][] GetTile(this TBin10 tbin, string s, int n)
    {
        List<object> list = null;
        for (var i = 0; i < tbin.Properties.Count; i++)
        {
            var property = tbin.Properties[i];
            if (property.Key != s) continue;
            list = property.Parse();
            break;
        }

        object[][] result = null;
        if (list is not null)
        {
            result = new object[list.Count / n][];
            for (int i = 0; i < list.Count / n; i++)
            {
                result[i] = new object[n];
            }

            for (int i = 0; i < list.Count; i++)
            {
                result[i / n][i % n] = list[i];
            }
        }

        return result;
    }

    public static List<string> GetPropertie(this TBin10 tbin)
    {
        List<string> list = new List<string>();
        List<Propertie>? properties = null;
        foreach (var la in tbin.Layers)
        {
            foreach (var t in la.Tiles)
            {
                switch (t)
                {
                    case null:
                        continue;
                    case StaticTile:

                        if (t is StaticTile s)
                        {
                            properties = s.Properties;
                        }

                        goto default;
                    case AnimatedTile:
                        if (t is AnimatedTile a)
                        {
                            properties = a.Properties;
                        }

                        goto default;
                    default:
                        if (properties == null)
                        {
                            break;
                        }

                        foreach (var pr in properties)
                        {
                            if (pr.Value is string ss && ss.Contains("Warp"))
                            {
                                string[] w = ss.Split(' ');
                                if (w.Length == 1)
                                {
                                    continue;
                                }

                                if (w[0] is "WarpWomensLocker" or "WarpMensLocker" or "LockedDoorWarp")
                                {
                                    list.Add(w[3]);
                                }
                                else if (w[0] == "Warp")
                                {
                                    if (!int.TryParse(w[1], out var sss))
                                    {
                                        w[3] = w[1];
                                    }

                                    list.Add(w[3]);
                                }
                                else if (w[0] == "MagicWarp")
                                {
                                    list.Add(w[1]);
                                }
                            }
                        }

                        properties = null;
                        break;
                }
            }
        }

        return list;
    }

    public static List<string>? GetWarpDirectedGraphAt(this TBin10 tbin, string name)
    {
        var t = tbin.GetTile("Warp", 5);
        var e = tbin.GetPropertie();
        if (t == null && e.Count == 0)
        {
            return null;
        }

        HashSet<string> set = new HashSet<string>();
        if (t != null)
        {
            foreach (var t1 in t)
            {
                var ss = (string)t1[2];
                if (name == ss)
                {
                    continue;
                }

                set.Add(ss);
            }
        }


        foreach (var s in e)
        {
            if (name == s)
            {
                continue;
            }

            set.Add(s);
        }

        List<string> l = set.ToList();
        if (l.Count > 0)
        {
            l.Insert(0, name);
            return l;
        }

        return null;
    }

    public static void GetWarpDirectedGraph(this List<TBin10> tbins, string[] names, out List<List<string>?> graph)
    {
        graph = new List<List<string>?>();
        for (var i = 0; i < tbins.Count; i++)
        {
            graph.Add(tbins[i].GetWarpDirectedGraphAt(names[i]));
        }
    }

    /*public static void RemoveRedundancyTiles(this TBin10 tbin, string path)
    {
        tbin.RemoveTileSheetsExtension();
        tbin.TileSheets.LoadImages(path, out var imgs, out var _);
        var ll = tbin.Layers;
        int len = ll[0].Tiles.Count;
        Dictionary<string, bool> map = new Dictionary<string, bool>();
        for (var j = 0; j < len; ++j)
        {
            bool[] bools = new bool[ll.Count];
            int i = ll.Count - 1;
            for (; i >= 0; i--)
            {
                var t = ll[i].Tiles[j];
                switch (ll[i].Tiles[j])
                {
                    case null:
                        bools[i] = false;
                        continue;
                    case StaticTile:
                        StaticTile s = t as StaticTile;
                        string id = s.TileSheet + s.TileIndex;
                        if (map.TryGetValue(id, out var value))
                        {
                            bools[i] = value;
                        }
                        else
                        {
                            bools[i] = imgs[s.TileSheet].All(image => image.IsRegionOpaque(s.TileIndex));
                            map.Add(id, bools[i]);
                        }

                        break;
                    case AnimatedTile:
                        AnimatedTile a = t as AnimatedTile;
                        bool[] b = new bool[a.Frames.Count];
                        for (var l = 0; l < a.Frames.Count; l++)
                        {
                            var s2 = a.Frames[l];
                            string id2 = s2.TileSheet + s2.TileIndex;
                            if (map.TryGetValue(id2, out var v2))
                            {
                                b[l] = v2;
                            }
                            else
                            {
                                b[l] = imgs[s2.TileSheet].All(image => image.IsRegionOpaque(s2.TileIndex));
                                map.Add(id2, bools[i]);
                            }
                        }

                        bools[i] = b.All(bb => bb);

                        break;
                    default:
                        throw new Exception("Bad tile data");
                }
            }

            for (i = ll.Count - 1; i >= 0; i--)
            {
                if (bools[i])
                {
                    i--;
                    break;
                }
            }

            for (; i >= 0; i--)
            {
                ll[i].Tiles[j] = null;
            }
        }

        foreach (var t in ll)
        {
            (t.Index, t._sizeArr, t._currTileSheet) = t.Tiles.CompileIndex(t.LayerSize.X);
        }
    }*/

    public static void ConsolidateNullTileSheets(this TBin10 tbin)
    {
        Dictionary<string,int> map = new Dictionary<string,int>();
        List<bool> tag = new List<bool>();
        for (var i = 0; i < tbin.TileSheets.Count; i++)
        {
            tag.Add(true);
            if (map.TryGetValue(tbin.TileSheets[i].Image,out var m))
            {
                if (tbin.TileSheets[i].Properties.Count > 0 && tbin.TileSheets[m].Properties.Count > 0)
                {
                    throw new NotImplementedException();
                }
                if (tbin.TileSheets[i].Properties.Count == 0 && tbin.TileSheets[m].Properties.Count == 0)
                {
                    tag[i] = false;
                }else if (tbin.TileSheets[i].Properties.Count > 0)
                {
                    map[tbin.TileSheets[i].Image] = i;
                    tag[m] = false;
                }
                else if(tbin.TileSheets[m].Properties.Count > 0)
                {
                    tag[i] = false;
                }
                else
                {
                    
                }
            }
            else
            {
                map.Add(tbin.TileSheets[i].Image,i);
            }
        }

        for (var i = 0; i < tbin.TileSheets.Count; i++)
        {
            if (tag[i]) continue;
            tag.RemoveAt(i);
            tbin.TileSheets.RemoveAt(i);
            i--;
        }
    }

    public static void RemoveRedundancyTileSheetProperties(this TBin10 tbin)
    {
        HashSet<string> set = new HashSet<string>();
        foreach (var layer in tbin.Layers)
        {
            foreach (var t in layer.Tiles)
            {
                switch (t)
                {
                    case null:
                        continue;
                    case StaticTile:

                        if (t is StaticTile s)
                        {
                            set.Add(s.GetId());
                        }
                        break;
                    case AnimatedTile:
                        if (t is AnimatedTile a)
                        {
                            foreach (var staticTile in a.Frames)
                            {
                                set.Add(staticTile.GetId());
                            }
                        }
                        break;
                    default:
                        throw new Exception("Bad tile data");
                }
            }
        }

        foreach (var sheet in tbin.TileSheets)
        {
            for (var i = 0; i < sheet.Properties.Count; i++)
            {
                var prop = sheet.Properties[i];
                var strId = sheet.Id + "@" + prop.Key.Split("@")[2];
                if (set.Contains(strId)) continue;
                sheet.Properties.RemoveAt(i);
                i--;
            }
        }
    }
}