using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Tracing;

/// <summary>截图 → 地图 + 坐标。基于图块指纹多数投票，不依赖 OpenCV。</summary>
public static class ScreenshotLocator
{
    public sealed record Options(
        int CropX = 0, int CropY = 0, int CropW = 0, int CropH = 0,
        string? MapFilter = null, int? ForceTileX = null, int? ForceTileY = null,
        int? ForceScale = null,
        double MinScore = 0.6, int MaxCandidates = 5);

    public sealed record Match(
        string Map, int TileX, int TileY, int Scale, int PhaseX, int PhaseY,
        double Score, int OffsetX, int OffsetY);

    public sealed record Box(int X, int Y, int W, int H);

    public sealed record Result(
        string Status, double Confidence, IReadOnlyList<Match> Candidates,
        int ImageWidth, int ImageHeight, int CropX, int CropY, int CropW, int CropH,
        int? RedBoxX, int? RedBoxY, int? RedBoxW, int? RedBoxH,
        IReadOnlyList<Box> RedBoxes);

    public static Result Locate(string imagePath, MapFingerprintIndex index, Options options)
    {
        byte[] px = Png.Read(imagePath, out int imageW, out int imageH);
        int cx = Math.Clamp(options.CropX, 0, Math.Max(0, imageW - 1));
        int cy = Math.Clamp(options.CropY, 0, Math.Max(0, imageH - 1));
        int cw = options.CropW > 0 ? Math.Min(options.CropW, imageW - cx) : imageW - cx;
        int ch = options.CropH > 0 ? Math.Min(options.CropH, imageH - cy) : imageH - cy;
        if (cw < 32 || ch < 32) throw new ArgumentException($"截图裁剪区域太小：{cw}×{ch}");

        bool[] red = BuildRedMask(px, imageW, imageH);
        List<Box> redBoxes = RedBoxes(red, imageW, imageH, cx, cy, cw, ch);
        Box? redBox = redBoxes.FirstOrDefault();

        if (options.ForceTileX is int fx && options.ForceTileY is int fy)
        {
            string? forcedMap = options.MapFilter;
            if (string.IsNullOrWhiteSpace(forcedMap))
                throw new ArgumentException("给了 --tile 就必须同时给 --map");
            var only = new Match(forcedMap, fx, fy, 1, 0, 0, 1.0, 0, 0);
            return new Result("matched", 1.0, new[] { only }, imageW, imageH, cx, cy, cw, ch,
                redBox?.X, redBox?.Y, redBox?.W, redBox?.H, redBoxes);
        }

        var votes = new Dictionary<(int Map, int OffX, int OffY, int Scale, int PhaseX, int PhaseY), int>();
        for (int scale = 1; scale <= 4; scale++)
        {
            if (options.ForceScale is int onlyScale && scale != onlyScale) continue;
            int tilePx = TileFingerprint.Tile * scale;
            int margin = tilePx * 2;
            int gridW = Math.Min(10, Math.Max(1, (cw - margin * 2) / tilePx));
            int gridH = Math.Min(8, Math.Max(1, (ch - margin * 2) / tilePx));
            if (gridW < 3 || gridH < 3) continue;

            // 相位是**屏幕像素**，X/Y 独立；相机偏移可能是任意像素，不能假设 4 的倍数。
            // 1px 步进：1px 的错位就足以让图块指纹换一张图，不能省。
            int phaseStep = 1;
            for (int phaseX = 0; phaseX < tilePx; phaseX += phaseStep)
            {
                for (int phaseY = 0; phaseY < tilePx; phaseY += phaseStep)
                {
                    for (int gy = 0; gy < gridH; gy++)
                    {
                        for (int gx = 0; gx < gridW; gx++)
                        {
                            int sx = margin + phaseX + gx * tilePx;
                            int sy = margin + phaseY + gy * tilePx;
                            ulong? hash = SampleHash(px, imageW, imageH, red, cx + sx, cy + sy, scale);
                            if (hash == null) continue;
                            if (index.CountOf(hash.Value) > 256) continue;

                            int hits = 0;
                            foreach (MapFingerprintIndex.TileHit hit in index.Lookup(hash.Value, 32))
                            {
                                if (options.MapFilter != null && !MatchMap(index, hit.Map, options.MapFilter)) continue;
                                var key = (hit.Map, hit.X - gx, hit.Y - gy, scale, phaseX, phaseY);
                                votes[key] = votes.TryGetValue(key, out int v) ? v + 1 : 1;
                                hits++;
                                if (hits >= 16) break;
                            }
                        }
                    }
                }
            }
        }

        var candidates = new List<Match>();
        var seen = new HashSet<(int, int, int, int)>();
        foreach (var kv in votes.OrderByDescending(k => k.Value).Take(400))
        {
            (int mapIdx, int offX, int offY, int scale, int phaseX, int phaseY) = kv.Key;
            if (mapIdx < 0 || mapIdx >= index.Maps.Count) continue;
            if (!seen.Add((mapIdx, offX, offY, scale))) continue;
            MapFingerprintIndex.MapEntry map = index.Maps[mapIdx];
            double score = Verify(map, offX, offY, scale, phaseX, phaseY, px, imageW, imageH, red, cx, cy, cw, ch);
            if (score <= 0) continue;
            int centerTileX = Math.Clamp(offX + cw / (2 * TileFingerprint.Tile * scale),
                0, Math.Max(0, map.TilesWide - 1));
            int centerTileY = Math.Clamp(offY + ch / (2 * TileFingerprint.Tile * scale),
                0, Math.Max(0, map.TilesHigh - 1));
            candidates.Add(new Match(map.Name, centerTileX, centerTileY, scale, phaseX, phaseY,
                score, offX, offY));
        }

        candidates = candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Map, StringComparer.Ordinal)
            .Take(Math.Max(1, options.MaxCandidates)).ToList();
        Match? best = candidates.FirstOrDefault();
        bool matched = best != null && best.Score >= options.MinScore;
        return new Result(matched ? "matched" : "candidates", best?.Score ?? 0, candidates,
            imageW, imageH, cx, cy, cw, ch, redBox?.X, redBox?.Y, redBox?.W, redBox?.H, redBoxes);
    }

    private static bool MatchMap(MapFingerprintIndex index, int mapIdx, string filter)
    {
        string name = index.Maps[mapIdx].Name;
        return name.Equals(filter, StringComparison.OrdinalIgnoreCase)
               || name.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || ("Maps/" + name).Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static double Verify(MapFingerprintIndex.MapEntry map, int offX, int offY, int scale,
        int phaseX, int phaseY, byte[] px, int imageW, int imageH, bool[] red,
        int cropX, int cropY, int cropW, int cropH)
    {
        int tilePx = TileFingerprint.Tile * scale;
        int margin = tilePx * 2;
        int gridW = Math.Min(10, Math.Max(1, (cropW - margin * 2) / tilePx));
        int gridH = Math.Min(8, Math.Max(1, (cropH - margin * 2) / tilePx));
        int total = 0, hit = 0;
        for (int gy = 0; gy < gridH; gy++)
        {
            for (int gx = 0; gx < gridW; gx++)
            {
                int sx = margin + phaseX + gx * tilePx;
                int sy = margin + phaseY + gy * tilePx;
                ulong? hash = SampleHash(px, imageW, imageH, red, cropX + sx, cropY + sy, scale);
                if (hash == null) continue;
                total++;
                int mx = gx + offX, my = gy + offY;
                if (mx < 0 || my < 0 || mx >= map.TilesWide || my >= map.TilesHigh) continue;
                if (map.Hashes[my * map.TilesWide + mx] == hash.Value) hit++;
            }
        }
        return total == 0 ? 0 : (double)hit / total;
    }

    /// <summary>把屏幕上的 scale×16 方块降采样成 16×16；红色标注超过 25% 时返回 null。</summary>
    private static ulong? SampleHash(byte[] px, int width, int height, bool[] red,
        int x0, int y0, int scale)
    {
        int tile = TileFingerprint.Tile;
        if (x0 < 0 || y0 < 0 || x0 + tile * scale > width || y0 + tile * scale > height) return null;
        byte[] cell = new byte[tile * tile * 4];
        int redCount = 0, total = 0;
        for (int y = 0; y < tile; y++)
        {
            for (int x = 0; x < tile; x++)
            {
                int r = 0, g = 0, b = 0, a = 0, n = 0;
                for (int yy = 0; yy < scale; yy++)
                {
                    int py = y0 + y * scale + yy;
                    for (int xx = 0; xx < scale; xx++)
                    {
                        int pxX = x0 + x * scale + xx;
                        int i = py * width + pxX;
                        if (red[i]) redCount++;
                        total++;
                        int c = i * 4;
                        r += px[c]; g += px[c + 1]; b += px[c + 2]; a += px[c + 3];
                        n++;
                    }
                }
                int o = (y * tile + x) * 4;
                cell[o] = (byte)(r / n);
                cell[o + 1] = (byte)(g / n);
                cell[o + 2] = (byte)(b / n);
                cell[o + 3] = (byte)(a / n);
            }
        }
        if (total > 0 && redCount * 4 > total) return null;
        return TileFingerprint.Compute(cell, tile, tile, 0, 0);
    }

    private static bool[] BuildRedMask(byte[] px, int width, int height)
    {
        bool[] mask = new bool[width * height];
        for (int i = 0; i < width * height; i++)
        {
            int c = i * 4;
            int r = px[c], g = px[c + 1], b = px[c + 2], a = px[c + 3];
            // 只认**高饱和纯红**的标注；棕色泥地 (139,71,39) 也满足“R 比 G/B 高”，
            // 用 130/45 的宽松阈值会把整条土路并进最大连通块（实测红框 bbox 撑到半张图）。
            mask[i] = a > 100 && r >= 220 && g <= 60 && b <= 60;
        }
        return mask;
    }

    private static List<Box> RedBoxes(bool[] mask, int width, int height,
        int cropX, int cropY, int cropW, int cropH)
    {
        // 不能拿全图红像素的 bbox：Joja 招牌、红屋顶会把框撑到整张图。
        // 取**连通块**；按像素数降序，框通常比箭头/红色贴图大。
        bool[] seen = new bool[width * height];
        var stack = new Stack<(int X, int Y)>();
        var found = new List<(Box Box, int Count)>();
        for (int y = cropY; y < cropY + cropH; y++)
        {
            for (int x = cropX; x < cropX + cropW; x++)
            {
                int idx = y * width + x;
                if (!mask[idx] || seen[idx]) continue;
                int minX = x, minY = y, maxX = x, maxY = y, count = 0;
                seen[idx] = true;
                stack.Push((x, y));
                while (stack.Count > 0)
                {
                    (int px, int py) = stack.Pop();
                    count++;
                    if (px < minX) minX = px;
                    if (py < minY) minY = py;
                    if (px > maxX) maxX = px;
                    if (py > maxY) maxY = py;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = px + dx, ny = py + dy;
                            if (nx < cropX || ny < cropY || nx >= cropX + cropW || ny >= cropY + cropH)
                                continue;
                            int ni = ny * width + nx;
                            if (!mask[ni] || seen[ni]) continue;
                            seen[ni] = true;
                            stack.Push((nx, ny));
                        }
                    }
                }
                if (count < 30) continue;
                int bw = maxX - minX + 1, bh = maxY - minY + 1;
                if (bw < 20 || bh < 20) continue;
                // 用户画的是**闭合框**：像素应主要落在 bbox 的四条边上。
                // 红色花朵/招牌是实心块，箭头是斜线，borderRatio 都低。
                int border = 0;
                for (int yy = minY; yy <= maxY; yy++)
                    for (int xx = minX; xx <= maxX; xx++)
                        if (mask[yy * width + xx]
                            && (xx == minX || xx == maxX || yy == minY || yy == maxY))
                            border++;
                double ratio = border / (double)Math.Max(1, 2 * bw + 2 * bh - 4);
                if (ratio >= 0.35)
                    found.Add((new Box(minX, minY, bw, bh), count));
            }
        }
        return found.OrderByDescending(x => x.Count).Select(x => x.Box).ToList();
    }
}
