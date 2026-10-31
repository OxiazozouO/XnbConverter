using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Frameworks;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 框架增量烘焙：AT 的家具/装饰/作物、MFM 邮件、CCCB 捆绑包、SF 建筑。
/// 用合成包跑完整 <see cref="FrameworkBaker.Bake"/>，断言新增行与幂等。
/// </summary>
public class FrameworkBakerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fw_" + Guid.NewGuid().ToString("N"));
    private readonly string _tree;
    private readonly string _mods;

    public FrameworkBakerTests()
    {
        _tree = Path.Combine(_root, "tree");
        _mods = Path.Combine(_root, "Mods");
        Directory.CreateDirectory(Path.Combine(_tree, "Data"));
        Directory.CreateDirectory(_mods);
        WriteJson("Data/Furniture.json", new JObject
        {
            ["1"] = "Chair/chair/-1/-1/4/350/-1/Chair/0//true",
            // 63 高的床：原版源矩形按 16px 网格，必须补到 64 才不越界。
            ["2"] = "Bed/bed/-1/-1/1/100/-1/Bed/0//true",
        });
        WriteJson("Data/AdditionalWallpaperFlooring.json", new JArray());
        WriteJson("Data/Objects.json", new JObject
        {
            ["Seed"] = new JObject
            {
                ["Name"] = "Seed", ["DisplayName"] = "Seed", ["Description"] = "d",
                ["Type"] = "Seed", ["Category"] = -74, ["Price"] = 10,
                ["Texture"] = "Maps/springobjects", ["SpriteIndex"] = 1,
            },
        });
        WriteJson("Data/Crops.json", new JObject
        {
            ["Seed"] = new JObject
            {
                ["Seasons"] = new JArray("spring"),
                ["DaysInPhase"] = new JArray(1, 1),
                ["HarvestItemId"] = "Crop",
                ["Texture"] = "Crops", ["SpriteIndex"] = 0,
            },
        });
        WriteJson("Data/Shops.json", new JObject
        {
            ["SeedShop"] = new JObject
            {
                ["Items"] = new JArray(new JObject
                {
                    ["Id"] = "(O)Seed", ["ItemId"] = "(O)Seed", ["Price"] = 10,
                }),
            },
        });
        WriteJson("Data/Fences.json", new JObject());
        WriteJson("Data/BigCraftables.json", new JObject());
        WriteJson("Data/CraftingRecipes.json", new JObject());
        WriteJson("Data/CookingRecipes.json", new JObject());
        WriteJson("Data/Mail.json", new JObject());
        WriteJson("Data/TriggerActions.json", new JArray());
        WriteJson("Data/Bundles.json", new JObject
        {
            ["Pantry/0"] = "Spring Crops/O 465 1/24 1/0//0",
        });
        WriteJson("Data/Buildings.json", new JObject());
        WriteJson("Data/Pets.json", new JObject());
        WriteJson("ContentHashes.json", new JObject());
        WritePng(Path.Combine(_tree, "Animals", "Test.png"), 128, 256);

        // Alternative Textures：家具 + 墙纸 + 作物
        string at = Pack("ATPack", "PeacefulEnd.AlternativeTextures");
        Model(at, "Textures/Chair", new JObject
        {
            ["ItemName"] = "Chair", ["Type"] = "Furniture",
            ["TextureWidth"] = 16, ["TextureHeight"] = 32, ["Variations"] = 1,
        }, 16, 32);
        WritePngColor(Path.Combine(at, "Textures", "Chair", "texture.png"), 16, 32, 255, 255, 255);
        Model(at, "Textures/Bed", new JObject
        {
            ["ItemName"] = "Bed", ["Type"] = "Furniture",
            ["TextureWidth"] = 64, ["TextureHeight"] = 63, ["Variations"] = 1,
        }, 64, 63);
        // 第二个 AT 包：同一件椅子的同图变体，用来验证 --dedup-cosmetics。
        string at2 = Pack("ATPack2", "PeacefulEnd.AlternativeTextures");
        Model(at2, "Textures/Chair", new JObject
        {
            ["ItemName"] = "Chair", ["Type"] = "Furniture",
            ["TextureWidth"] = 16, ["TextureHeight"] = 32, ["Variations"] = 1,
        }, 16, 32);
        WritePngColor(Path.Combine(at2, "Textures", "Chair", "texture.png"), 16, 32, 255, 255, 255);
        // 第三个包：同基础但画面差很大的椅子，不能被相似去重误杀。
        string at3 = Pack("ATPack3", "PeacefulEnd.AlternativeTextures");
        Model(at3, "Textures/Chair", new JObject
        {
            ["ItemName"] = "Chair", ["Type"] = "Furniture",
            ["TextureWidth"] = 16, ["TextureHeight"] = 32, ["Variations"] = 1,
        }, 16, 32);
        WritePngColor(Path.Combine(at3, "Textures", "Chair", "texture.png"), 16, 32, 255, 0, 0);
        Model(at, "Textures/Wall", new JObject
        {
            ["ItemName"] = "Wallpaper", ["Type"] = "Decoration",
            ["TextureWidth"] = 16, ["TextureHeight"] = 48, ["Variations"] = 2,
        }, 256, 96);
        Model(at, "Textures/Crop", new JObject
        {
            ["ItemName"] = "Seed", ["Type"] = "Crop",
            ["TextureWidth"] = 128, ["TextureHeight"] = 32, ["Variations"] = 2,
        }, 128, 64);

        // Mail Framework
        string mfm = Pack("MFMPack", "DIGUS.MailFrameworkMod");
        WriteFile(Path.Combine(mfm, "mail.json"),
            """[{"Id":"TestMail","Title":"T","Text":"Hello"}]""");

        // CCCB
        string cccb = Pack("CCCBPack", "alja.CCCB");
        WriteFile(Path.Combine(cccb, "content.json"),
            """{"Pure_Vanilla":{"Spring Crops":"New/Reward/Value"}}""");

        // Custom Companions
        string cc = Pack("CCPack", "PeacefulEnd.CustomCompanions");
        string cdir = Path.Combine(cc, "Companions", "TestPet");
        Directory.CreateDirectory(cdir);
        WriteFile(Path.Combine(cdir, "companion.json"),
            """{"Name":"TestPet","TileSheetPath":"Animals\\Test","FrameSizeWidth":32,"FrameSizeHeight":32,"TravelSpeed":2,"IdleBehavior":"Wander","Sounds":[{"SoundName":"dog_bark","WhenToPlay":"Idle"},{"SoundName":"waterSlosh","WhenToPlay":"Moving"}]}""");
        string stillDir = Path.Combine(cc, "Companions", "StillPet");
        Directory.CreateDirectory(stillDir);
        WriteFile(Path.Combine(stillDir, "companion.json"),
            """{"Name":"StillPet","TileSheetPath":"Animals\\Test","FrameSizeWidth":32,"FrameSizeHeight":32,"IdleBehavior":"Nothing"}""");
        string paceDir = Path.Combine(cc, "Companions", "PacePet");
        Directory.CreateDirectory(paceDir);
        WriteFile(Path.Combine(paceDir, "companion.json"),
            """{"Name":"PacePet","TileSheetPath":"Animals\\Test","FrameSizeWidth":32,"FrameSizeHeight":32,"IdleBehavior":"Pacing"}""");
        string defaultDir = Path.Combine(cc, "Companions", "DefaultPet");
        Directory.CreateDirectory(defaultDir);
        WriteFile(Path.Combine(defaultDir, "companion.json"),
            """{"Name":"DefaultPet","TileSheetPath":"Animals\\Test","FrameSizeWidth":32,"FrameSizeHeight":32}""");
        string wanderDir = Path.Combine(cc, "Companions", "WanderPet");
        Directory.CreateDirectory(wanderDir);
        WriteFile(Path.Combine(wanderDir, "companion.json"),
            """{"Name":"WanderPet","TileSheetPath":"Animals\\Test","FrameSizeWidth":32,"FrameSizeHeight":32,"IdleBehavior":"Wander","MinHaltTime":1500,"MaxHaltTime":4000}""");
        string flyingDir = Path.Combine(cc, "Companions", "FlyingPet");
        Directory.CreateDirectory(flyingDir);
        WriteFile(Path.Combine(flyingDir, "companion.json"),
            """{"Name":"FlyingPet","TileSheetPath":"Animals\\Test","FrameSizeWidth":32,"FrameSizeHeight":32,"Type":"Flying","IdleBehavior":"Wander"}""");

        // Solid Foundations
        string sf = Pack("SFPack", "PeacefulEnd.SolidFoundations");
        string bdir = Path.Combine(sf, "Buildings", "Test");
        Directory.CreateDirectory(Path.Combine(bdir, "Skins"));
        WriteFile(Path.Combine(bdir, "building.json"),
            """{"Id":"TestBuilding","Name":"Test","Description":"D","BuildCost":100,"Size":{"X":3,"Y":1},"SourceRect":"0 0 80 48","CollisionMap":"XOX","Skins":[{"ID":"Skin1","Name":"Skin","Texture":"Skin"}]}""");
        WritePng(Path.Combine(bdir, "building.png"), 80, 48);
        WritePng(Path.Combine(bdir, "Skins", "Skin.png"), 80, 48);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Pack(string name, string uid)
    {
        string dir = Path.Combine(_mods, name);
        Directory.CreateDirectory(dir);
        WriteFile(Path.Combine(dir, "manifest.json"), new JObject
        {
            ["Name"] = name,
            ["UniqueID"] = "test." + name,
            ["Version"] = "1.0.0",
            ["ContentPackFor"] = new JObject { ["UniqueID"] = uid },
        }.ToString());
        return dir;
    }

    private static void Model(string pack, string rel, JObject model, int w, int h)
    {
        string dir = Path.Combine(pack, rel);
        Directory.CreateDirectory(dir);
        WriteFile(Path.Combine(dir, "texture.json"), model.ToString());
        WritePng(Path.Combine(dir, "texture.png"), w, h);
    }

    private void WriteJson(string rel, JToken token) => WriteFile(Path.Combine(_tree, rel), token.ToString());

    private static void WriteFile(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static void WritePng(string path, int w, int h)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Png.Write(path, new byte[w * h * 4], w, h);
    }

    private static void WritePngColor(string path, int w, int h, byte r, byte g, byte b)
    {
        var data = new byte[w * h * 4];
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i] = r; data[i + 1] = g; data[i + 2] = b; data[i + 3] = 255;
        }
        Png.Write(path, data, w, h);
    }

    /// <summary>白底上画一块纯色局部补丁：模拟“同一件家具只换了书脊/花色”的变体。</summary>
    private static void WritePngPatch(string path, int w, int h, int px, int py, int pw, int ph)
    {
        var data = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                bool inside = x >= px && x < px + pw && y >= py && y < py + ph;
                data[i] = inside ? (byte)255 : (byte)255;
                data[i + 1] = inside ? (byte)0 : (byte)255;
                data[i + 2] = inside ? (byte)0 : (byte)255;
                data[i + 3] = 255;
            }
        Png.Write(path, data, w, h);
    }

    private JObject Read(string rel) => JObject.Parse(File.ReadAllText(Path.Combine(_tree, rel)));
    private JArray ReadArr(string rel) => JArray.Parse(File.ReadAllText(Path.Combine(_tree, rel)));

    [Fact]
    public void SpaceCore对象扩展映射到Objects并镜像中文结构字段()
    {
        JObject baseObjects = JObject.Parse(File.ReadAllText(Path.Combine(_tree, "Data/Objects.json")));
        baseObjects["test.SpacePack_shield"] = new JObject
        {
            ["Name"] = "Shield",
            ["CanBeTrashed"] = true,
            ["CanBeGivenAsGift"] = true,
        };
        WriteJson("Data/Objects.json", baseObjects);
        WriteJson("Data/Objects.zh-CN.json", new JObject
        {
            ["test.SpacePack_shield"] = new JObject
            {
                ["Name"] = "盾牌",
                ["CanBeTrashed"] = true,
                ["CanBeGivenAsGift"] = true,
            },
        });

        string pack = Pack("SpacePack", "Pathoschild.ContentPatcher");
        WriteFile(Path.Combine(pack, "content.json"), """
            {
              "Changes": [
                { "Action": "EditData",
                  "Target": "spacechase0.SpaceCore/ObjectExtensionData",
                  "Entries": {
                    "{{ModID}}_shield": {
                      "CanBeTrashed": false,
                      "CanBeGivenAsGift": false,
                      "CanBeShipped": false,
                      "MaxStackSizeOverride": 1
                    }
                  } }
              ]
            }
            """);

        FrameworkBaker.Bake(_tree, _mods);

        JObject after = Read("Data/Objects.json");
        JObject shield = (JObject)after["test.SpacePack_shield"]!;
        Assert.False(shield.Value<bool>("CanBeTrashed"));
        Assert.False(shield.Value<bool>("CanBeGivenAsGift"));
        Assert.True(shield.Value<bool>("ExcludeFromShippingCollection"));

        JObject zh = Read("Data/Objects.zh-CN.json");
        JObject zhShield = (JObject)zh["test.SpacePack_shield"]!;
        Assert.False(zhShield.Value<bool>("CanBeTrashed"));
        Assert.False(zhShield.Value<bool>("CanBeGivenAsGift"));
        Assert.True(zhShield.Value<bool>("ExcludeFromShippingCollection"));
        Assert.Equal("盾牌", zhShield.Value<string>("Name"));
    }

    [Fact]
    public void SpaceCore作物RegrowDays映射并镜像中文()
    {
        JObject baseCrops = JObject.Parse(File.ReadAllText(Path.Combine(_tree, "Data/Crops.json")));
        baseCrops["test.SpaceCropPack_seed"] = new JObject
        {
            ["Seasons"] = new JArray("spring"),
            ["DaysInPhase"] = new JArray(1),
            ["HarvestItemId"] = "Crop",
            ["RegrowDays"] = -1,
        };
        WriteJson("Data/Crops.json", baseCrops);
        WriteJson("Data/Crops.zh-CN.json", new JObject
        {
            ["test.SpaceCropPack_seed"] = new JObject
            {
                ["Seasons"] = new JArray("spring"),
                ["DaysInPhase"] = new JArray(1),
                ["HarvestItemId"] = "Crop",
                ["RegrowDays"] = -1,
            },
        });

        string pack = Pack("SpaceCropPack", "Pathoschild.ContentPatcher");
        WriteFile(Path.Combine(pack, "content.json"), """
            {
              "Changes": [
                { "Action": "EditData",
                  "Target": "spacechase0.SpaceCore/CropExtensionData",
                  "Entries": {
                    "{{ModID}}_seed": {
                      "YieldOverrides": { "4": { "RegrowDays": 5 } }
                    }
                  } }
              ]
            }
            """);

        var probe = (JObject)baseCrops.DeepClone();
        var probeNotes = new List<string>();
        Assert.Equal(1, SpaceCoreCropExtensions.Apply(probe, pack, "test.SpaceCropPack", probeNotes));

        FrameworkBaker.Bake(_tree, _mods);

        Assert.Equal(5, Read("Data/Crops.json")["test.SpaceCropPack_seed"]!["RegrowDays"]!.Value<int>());
        Assert.Equal(5, Read("Data/Crops.zh-CN.json")["test.SpaceCropPack_seed"]!["RegrowDays"]!.Value<int>());
    }

    [Fact]
    public void SpaceCore纹理覆盖重复跑不叠加并写文件哈希缓存()
    {
        string target = Path.Combine(_tree, "LooseSprites", "SC_Target.png");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        WritePngColor(target, 16, 16, 0, 0, 0);
        string source = Path.Combine(_tree, "Assets", "SC_Source.png");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        var sourcePixels = new byte[16 * 16 * 4];
        for (int i = 0; i < sourcePixels.Length; i += 4)
        {
            sourcePixels[i] = 255;
            sourcePixels[i + 3] = 128;
        }
        Png.Write(source, sourcePixels, 16, 16);

        string pack = Pack("TexturePack", "Pathoschild.ContentPatcher");
        WriteFile(Path.Combine(pack, "content.json"), """
            {
              "Changes": [
                { "Action": "EditData",
                  "Target": "spacechase0.SpaceCore/TextureOverrides",
                  "Entries": {
                    "SC_Test": {
                      "TargetTexture": "LooseSprites/SC_Target",
                      "SourceTexture": "Assets/SC_Source",
                      "TargetRect": { "X": 0, "Y": 0, "Width": 16, "Height": 16 }
                    }
                  } }
              ]
            }
            """);

        FrameworkBaker.Result first = FrameworkBaker.Bake(_tree, _mods);
        Assert.True(first.Textures >= 1);
        byte[] once = File.ReadAllBytes(target);
        byte[] pixels = Png.Read(target, out _, out _);
        Assert.InRange(pixels[0], 1, 254);
        Assert.Equal(0, pixels[1]);
        Assert.Equal(0, pixels[2]);

        // 删掉像素级状态，只留文件级哈希；第二次仍不能把半透明帧再叠一次。
        File.Delete(Path.Combine(_root, "_texture-overrides.json"));
        FrameworkBaker.Bake(_tree, _mods);
        Assert.Equal(once, File.ReadAllBytes(target));
        Assert.True(File.Exists(Path.Combine(_root, "_texture-overrides-files.json")));
    }

    [Fact]
    public void 同伴源图变化会失效帧表缓存并重建()
    {
        FrameworkBaker.Bake(_tree, _mods);
        string sheetPath = Directory.EnumerateFiles(
            Path.Combine(_tree, "Mods"), "TestPet_pet32.png", SearchOption.AllDirectories).Single();
        byte[] before = File.ReadAllBytes(sheetPath);
        string statePath = Path.Combine(_root, "_cc-sheets.json");
        Assert.True(File.Exists(statePath));

        WritePngColor(Path.Combine(_tree, "Animals", "Test.png"), 128, 256, 255, 0, 0);
        FrameworkBaker.Bake(_tree, _mods);
        byte[] changed = File.ReadAllBytes(sheetPath);
        Assert.NotEqual(before, changed);

        FrameworkBaker.Bake(_tree, _mods);
        Assert.Equal(changed, File.ReadAllBytes(sheetPath));
    }

    [Fact]
    public void 四类框架都新增行且重复跑幂等()
    {
        FrameworkBaker.Result result = FrameworkBaker.Bake(_tree, _mods,
            dedupCosmetics: true, dedupThreshold: 6);

        Assert.Equal(3, result.Furniture);
        Assert.Equal(1, result.Deduped);       // 第二个包的椅子与第一个同图；红色椅子保留
        Assert.Equal(1, result.Decoration);
        Assert.Equal(2, result.Crop);
        Assert.Equal(1, result.Mail);
        // 只替换已有 `Area/Index` 条目；没有原版键的新捆绑包不能新建
        // （`CCCB_x` 键会让 CommunityCenter 的 SetBundleData 越界）。
        Assert.Equal(1, result.Bundles);
        Assert.Equal(1, result.Buildings);
        Assert.Equal(6, result.Pets);

        JObject furniture = Read("Data/Furniture.json");
        JProperty at = furniture.Properties().First(p => p.Name.Contains("_Chair_"));
        string[] fields = at.Value.Value<string>()!.Split('/');
        Assert.Equal("1 2", fields[2]);
        Assert.Contains("\\", fields[9]);
        Assert.True(File.Exists(Path.Combine(_tree, fields[9].Replace('\\', '/') + ".png")));
        Assert.True(File.Exists(Path.Combine(_tree, fields[9] + ".config")));

        JProperty bed = furniture.Properties().Single(p => p.Name.Contains("_Bed_"));
        string[] bedFields = bed.Value.Value<string>()!.Split('/');
        Assert.Equal("4 4", bedFields[2]);       // 64×63 -> 补到 64×64
        Png.Read(Path.Combine(_tree, bedFields[9].Replace('\\', '/') + ".png"),
            out int bw, out int bh);
        Assert.Equal((64, 64), (bw, bh));

        JObject crops = Read("Data/Crops.json");
        Assert.Equal(2, crops.Properties().Count(p => p.Name.StartsWith("AT_")));
        JObject shops = Read("Data/Shops.json");
        JArray items = (JArray)shops["SeedShop"]!["Items"]!;
        Assert.Equal(3, items.Count);   // 基础 1 + 两个 AT 种子

        Assert.NotNull(Read("Data/Mail.json")["TestMail"]);
        Assert.Contains(ReadArr("Data/TriggerActions.json").OfType<JObject>(),
            t => t.Value<string>("Action") == "AddMail Current TestMail");

        Assert.Equal("New/Reward/Value", Read("Data/Bundles.json").Value<string>("Pantry/0"));
        JObject pet = (JObject)Read("Data/Pets.json")["CC_test.CCPack_TestPet"]!;
        Assert.Equal("dog_bark", pet.Value<string>("BarkSound"));
        JObject petWalk = (JObject)((JArray)pet["Behaviors"]!)[0]!;
        Assert.Equal("waterSlosh", petWalk.Value<string>("SoundOnStart"));
        JObject still = (JObject)Read("Data/Pets.json")["CC_test.CCPack_StillPet"]!;
        JObject stillBehavior = (JObject)((JArray)still["Behaviors"]!)[0]!;
        Assert.Equal("Idle", stillBehavior.Value<string>("Id"));
        JObject pace = (JObject)Read("Data/Pets.json")["CC_test.CCPack_PacePet"]!;
        JObject paceBehavior = (JObject)((JArray)pace["Behaviors"]!)[0]!;
        Assert.True(paceBehavior.Value<bool>("IsSideBehavior"));
        JObject defaultPet = (JObject)Read("Data/Pets.json")["CC_test.CCPack_DefaultPet"]!;
        JObject defaultBehavior = (JObject)((JArray)defaultPet["Behaviors"]!)[0]!;
        Assert.Equal("Idle", defaultBehavior.Value<string>("Id"));
        JObject wander = (JObject)Read("Data/Pets.json")["CC_test.CCPack_WanderPet"]!;
        JArray wanderBehaviors = (JArray)wander["Behaviors"]!;
        Assert.Equal(2, wanderBehaviors.Count);
        Assert.Equal("Walk", wanderBehaviors[0]!.Value<string>("Id"));
        Assert.Equal("Idle", wanderBehaviors[1]!.Value<string>("Id"));
        JObject flying = (JObject)Read("Data/Pets.json")["CC_test.CCPack_FlyingPet"]!;
        Assert.Single((JArray)flying["Behaviors"]!);   // 飞行同伴不 halt

        JObject building = (JObject)Read("Data/Buildings.json")["TestBuilding"]!;
        Assert.IsType<JObject>(building["SourceRect"]);
        Assert.Single((JArray)building["Skins"]!);

        JObject hashes = Read("ContentHashes.json");
        Assert.Contains(hashes.Properties(), p => p.Name.Contains("/AT/", StringComparison.Ordinal));

        // 第二次不重复追加
        FrameworkBaker.Result second = FrameworkBaker.Bake(_tree, _mods,
            dedupCosmetics: true, dedupThreshold: 6);
        Assert.Equal(0, second.Pets);
        Assert.Equal(0, second.Furniture + second.Decoration + second.Crop + second.Mail +
                        second.Bundles + second.Buildings);
        Assert.Equal(3, Read("Data/Furniture.json").Properties().Count(p => p.Name.StartsWith("AT_")));
    }

    [Fact]
    public void MoreGrass变体覆盖进原版草图集()
    {
        Directory.CreateDirectory(Path.Combine(_tree, "TerrainFeatures"));
        Png.Write(Path.Combine(_tree, "TerrainFeatures", "grass.png"),
            new byte[66 * 240 * 4], 66, 240);

        string mg = Pack("MGPack", "EpicBellyFlop45.MoreGrass");
        (string Dir, byte R, byte G, byte B)[] seasons =
        {
            ("spring", 10, 200, 10), ("summer", 200, 200, 10),
            ("fall", 200, 80, 10), ("winter", 10, 200, 200),
        };
        foreach ((string dir, byte r, byte g, byte b) in seasons)
        {
            string d = Path.Combine(mg, dir);
            Directory.CreateDirectory(d);
            for (int i = 0; i < 3; i++)
                WritePngColor(Path.Combine(d, $"grass_{dir}_{i}.png"), 15, 20,
                    (byte)(r + i), (byte)(g + i), (byte)(b + i));
        }

        FrameworkBaker.Result first = FrameworkBaker.Bake(_tree, _mods);
        Assert.Equal(4, first.Grass);

        byte[] sheet = Png.Read(Path.Combine(_tree, "TerrainFeatures", "grass.png"),
            out int w, out int h);
        Assert.Equal((66, 240), (w, h));
        for (int season = 0; season < 4; season++)
        {
            int y = season switch { 0 => 0, 1 => 20, 2 => 40, _ => 80 };
            int i = (y * w) * 4;
            Assert.Equal(seasons[season].R, sheet[i]);
            Assert.Equal(seasons[season].G, sheet[i + 1]);
            Assert.Equal(seasons[season].B, sheet[i + 2]);
        }

        // 第二次像素没变 ⇒ 不再报吸收数。
        FrameworkBaker.Result second = FrameworkBaker.Bake(_tree, _mods);
        Assert.Equal(0, second.Grass);
    }

    [Fact]
    public void 旧版去重侧车不会短路新判据()
    {
        FrameworkBaker.Bake(_tree, _mods, dedupCosmetics: true, dedupThreshold: 6);

        JObject furniture = Read("Data/Furniture.json");
        JProperty kept = furniture.Properties().Single(p => p.Name.Contains("ATPack3"));
        string keptId = kept.Name;
        furniture.Remove(keptId);
        WriteJson("Data/Furniture.json", furniture);

        // 模拟旧版侧车：参数相同但没有 version，ids 里写着本轮应保留的红椅子。
        WriteFile(Path.Combine(_root, "_frameworks-dedup.json"), new JObject
        {
            ["threshold"] = 6,
            ["colorTolerance"] = 0.0,
            ["maxPixelDifference"] = 1.0,
            ["colorConsistency"] = 2.0,
            ["ids"] = new JArray(keptId),
        }.ToString());

        FrameworkBaker.Bake(_tree, _mods, dedupCosmetics: true, dedupThreshold: 6);

        JObject after = Read("Data/Furniture.json");
        Assert.Contains(after.Properties(), p => p.Name.Contains("ATPack3"));
        // 该去的仍然要去：同图变体不能因为忽略旧侧车就被补回来。
        Assert.DoesNotContain(after.Properties(), p => p.Name.Contains("ATPack2"));
    }

    [Fact]
    public void 不同季节的同款变体不互相去重()
    {
        string fall = Pack("ATFallBed", "PeacefulEnd.AlternativeTextures");
        Model(fall, "Textures/Bed", new JObject
        {
            ["ItemName"] = "Bed", ["Type"] = "Furniture",
            ["TextureWidth"] = 16, ["TextureHeight"] = 16, ["Variations"] = 1,
            ["Seasons"] = new JArray("Fall"),
        }, 16, 16);
        WritePngColor(Path.Combine(fall, "Textures", "Bed", "texture.png"), 16, 16, 255, 255, 255);

        string spring = Pack("ATSpringBed", "PeacefulEnd.AlternativeTextures");
        Model(spring, "Textures/Bed", new JObject
        {
            ["ItemName"] = "Bed", ["Type"] = "Furniture",
            ["TextureWidth"] = 16, ["TextureHeight"] = 16, ["Variations"] = 1,
            ["Seasons"] = new JArray("Spring"),
        }, 16, 16);
        WritePngColor(Path.Combine(spring, "Textures", "Bed", "texture.png"), 16, 16, 255, 0, 0);

        FrameworkBaker.Bake(_tree, _mods,
            dedupCosmetics: true, dedupThreshold: 6, dedupColorTolerance: 18);

        // 纯换色（白→红）如果跨季节比较会被当成重复；季节分组后必须两个都留。
        JObject furniture = Read("Data/Furniture.json");
        JProperty[] beds = furniture.Properties()
            .Where(p => p.Name.StartsWith("AT_") && p.Name.Contains("_Bed_")).ToArray();
        Assert.Contains(beds, p => p.Name.Contains("ATFallBed"));
        Assert.Contains(beds, p => p.Name.Contains("ATSpringBed"));
    }

    [Fact]
    public void 纯变色去重不误杀局部花色()
    {
        string at4 = Pack("ATPack4", "PeacefulEnd.AlternativeTextures");
        Model(at4, "Textures/Chair", new JObject
        {
            ["ItemName"] = "Chair", ["Type"] = "Furniture",
            ["TextureWidth"] = 16, ["TextureHeight"] = 32, ["Variations"] = 1,
        }, 16, 32);
        // 白椅 + 左上角 8×8 红块：可见的局部改动，不是整图换色。
        WritePngPatch(Path.Combine(at4, "Textures", "Chair", "texture.png"), 16, 32, 0, 0, 8, 8);

        FrameworkBaker.Result result = FrameworkBaker.Bake(_tree, _mods,
            dedupCosmetics: true, dedupThreshold: 6, dedupColorTolerance: 18);

        // 同图（Pack2）与整图纯换色（Pack3）应去重；局部红块（Pack4）必须保留。
        Assert.Equal(2, result.Deduped);
        JObject furniture = Read("Data/Furniture.json");
        JProperty[] chairs = furniture.Properties()
            .Where(p => p.Name.Contains("_Chair_")).ToArray();
        Assert.Equal(2, chairs.Length);
        Assert.Contains(chairs, p => p.Name.Contains("ATPack4"));
    }
}

