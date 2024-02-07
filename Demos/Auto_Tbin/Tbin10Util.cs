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

    public static void ConsolidateLayers(this TBin10 tbin, string path)
    {
        var sb = new StringBuilder();
        var list = new Dictionary<string, Image<Rgba32>>();
        foreach (var v in tbin.TileSheets)
        {
            v.Image = v.Image.Replace(".png", "");
            var s = Path.Combine(path, v.Image + ".png");
            var image = Image.Load<Rgba32>(s);
            list.Add(v.Id, image);
        }

        var map = new Dictionary<string, List<Layer>>();
        foreach (var la in tbin.Layers)
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

        var properties = new Dictionary<string, List<Propertie>>();
        foreach (var sheet in tbin.TileSheets)
        foreach (var prop in sheet.Properties)
        {
            var strId = sheet.Id + "_" + prop.Key.Split("@")[2];
            if (!properties.ContainsKey(strId))
            {
                properties[strId] = new List<Propertie>();
            }

            properties[strId].Add(prop);
        }

        var imgList = new ImgList(path);
        var indexed = new Dictionary<string, (int, int)>();
        var layers = new List<Layer>();


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
                Tiles = new List<BaseTile>(),
                Index = new List<char>(),
                _sizeArr = new List<int>(),
                _currTileSheet = new List<string>()
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
                                    sb.Append(an.TileSheet).Append('_').Append(an.TileIndex).Append('_');
                            }


                            if (indexed.TryGetValue(sb.ToString(), out var value))
                            {
                                layer.Tiles.Add(layers[value.Item1].Tiles[value.Item2]);
                            }


                            else
                            {
                                var image = new Image<Rgba32>(16, 16);
                                var pl = new List<Propertie>();
                                for (var i = 0; i < staInd; ++i)
                                {
                                    image.DrawImagePortion(0, list[staticTile[i].TileSheet], staticTile[i].TileIndex);
                                }

                                for (var i = 0; i < staInd; ++i)
                                {
                                    var strId = staticTile[i].TileSheet + "_" + staticTile[i].TileIndex;
                                    if (properties.TryGetValue(strId, out var property))
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
                                        _frameCount = ma,
                                        Index = new List<char>(),
                                        _currTileSheet = new List<string>()
                                    };
                                    for (var i = 0; i < ma; i++)
                                    {
                                        var imgCopy = image.Clone();
                                        for (var k = 0; k < anmInd; k++)
                                        {
                                            var frame = animatedTile[k].Frames[arr[k + 1][i]];
                                            imgCopy.DrawImagePortion(0, list[frame.TileSheet], frame.TileIndex);
                                        }

                                        var pll = new List<Propertie>();
                                        pll.AddRange(pl.Select(tmp => new Propertie
                                            { Key = tmp.Key, Type = tmp.Type, Value = tmp.Value }));
                                        for (var k = 0; k < anmInd; k++)
                                        {
                                            var vvv = animatedTile[k].Frames[arr[k + 1][i]];
                                            var strId = vvv.TileSheet + "_" + vvv.TileIndex;
                                            if (properties.TryGetValue(strId, out var proper))
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

                                    CompileIndex(anTile.Frames, anTile.Index, null, anTile._currTileSheet,
                                        layer.LayerSize.X);
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

            CompileIndex(layer.Tiles, layer.Index, layer._sizeArr, layer._currTileSheet, layer.LayerSize.X);
        }

        tbin.Layers = layers;
        imgList.Save();
        tbin.TileSheets.AddRange(imgList.TileSheets);
    }

    private static void CompileIndex<T>(List<T> tiles, List<char> index, List<int> sizeArr, List<string> currTileSheet,
        int w)
    {
        if (currTileSheet.Count == 0)
        {
            currTileSheet.Add("");
        }

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


    public class ImgList
    {
        private const int DefSize = 1250;
        private readonly List<List<Image<Rgba32>>> _list;
        private readonly string _path;

        private readonly List<string> _pathNames;
        public readonly List<TileSheet> TileSheets;
        private List<Image<Rgba32>> _tmpList;
        private TileSheet _tmpTile;

        public ImgList(string path)
        {
            _path = path;
            _tmpList = new List<Image<Rgba32>>();
            _list = new List<List<Image<Rgba32>>>();
            _list.Add(_tmpList);
            _pathNames = new List<string>();
            _pathNames.Add(GetImgName());

            _tmpTile = new TileSheet
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
            TileSheets = new List<TileSheet>();
            TileSheets.Add(_tmpTile);
        }

        public void Add(Image<Rgba32> img)
        {
            if (_tmpList.Count == DefSize * DefSize)
            {
                Save();
                _tmpList = new List<Image<Rgba32>>();
                _pathNames.Add(GetImgName());
                TileSheets.Add(_tmpTile);

                _tmpTile = new TileSheet
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

            _tmpList.Add(img);
        }

        public void AddProperties(List<Propertie> pl)
        {
            _tmpTile.Properties.AddRange(pl);
        }

        public void Save()
        {
            int num;
            if (_tmpList.Count < DefSize * DefSize)
            {
                num = (int)Math.Sqrt(_tmpList.Count);
                if (num * num < _tmpList.Count) ++num;

                num *= 16;
            }
            else
            {
                num = DefSize * 16;
            }

            var m = new Image<Rgba32>(num, num); //2
            TileSheets[^1].SheetSize = new IntVector2 { X = num / 16, Y = num / 16 };
            for (var i = 0; i < _tmpList.Count; i++) m.DrawImagePortion(i, _tmpList[i], 0);

            _list.Add(_tmpList);

            m.Save(Path.Combine(_path, _pathNames[^1] + ".png"));
        }

        public string NowName()
        {
            return _pathNames[^1];
        }

        public int NowIndex()
        {
            return _tmpList.Count - 1;
        }
    }
}