#!/usr/bin/env node
/**
 * 清理 `D:\tmp` —— 我（Claude）干活时在那儿堆的临时产物。
 *
 * 用法：
 *     node tools/clean-cache.js            # 真删（列出并删掉 KEEP 之外的一切）
 *     node tools/clean-cache.js --dry      # 只看要删什么
 *     node tools/clean-cache.js --size     # 顺带算占用（要递归遍历，慢）
 *
 * **白名单式**：只有 KEEP 里的留着，`D:\tmp` 下**其余全清**。
 * 早先是反过来列「要删的」，结果每轮都要回头补登、还老漏；白名单不会漏。
 *
 * KEEP 的取舍标准：**重跑成本高、又常被别的实验复用**的。
 * 解包树、产出树、反编译产物、探针工程、脚本输出、日志 —— 这些都能随时重做，一律清。
 *
 * 安全边界：
 *   · **只碰 `D:\tmp`** 这一层，不递归别人的目录
 *   · **`.` 开头的（隐藏文件/目录）一律保留**，不猜
 *   · 仓库、`D:\XnbConverter`、`D:\Stardew.Valley.v1.6.15`、`D:\sdv_tree` 都不在范围内
 */

const fs = require('fs');
const path = require('path');

const TMP = process.env.TMP_ROOT || 'D:\\tmp';
const DRY = process.argv.includes('--dry');
const WITH_SIZE = process.argv.includes('--size');

/**
 * 白名单：这些留着。
 * ⚠ 往里加东西前先想清楚 —— 加进来就意味着「以后清理不会动它」。
 */
const KEEP = [
  'content3', 'content5',   // 原版 Content 的解包树 —— 工具默认的对比基准，很多脚本要读它
  'gv', 'gv2',              // 游戏副本 —— 实机验证要用（gv2 的 Content/XACT 已恢复成原版）
  'cp_decompile',           // Content Patcher 反编译 —— docs 里多处拿它当源码依据，别删
];

function globToRegex(glob) {
  const escaped = glob.replace(/[.+^${}()|[\]\\]/g, '\\$&')
    .replace(/\*/g, '[^\\\\/]*')
    .replace(/\?/g, '[^\\\\/]');
  return new RegExp('^' + escaped + '$', 'i');
}

function rm(target) {
  if (fs.lstatSync(target).isDirectory()) {
    fs.rmSync(target, { recursive: true, force: true });
  } else {
    fs.unlinkSync(target);
  }
}

function sizeOf(target) {
  try {
    const stat = fs.lstatSync(target);
    if (!stat.isDirectory()) return stat.size;
    let total = 0;
    for (const entry of fs.readdirSync(target, { withFileTypes: true })) {
      total += sizeOf(path.join(target, entry.name));
    }
    return total;
  } catch {
    return 0;
  }
}

function human(bytes) {
  const units = ['B', 'KB', 'MB', 'GB'];
  let n = bytes, i = 0;
  while (n >= 1024 && i < units.length - 1) { n /= 1024; i++; }
  return `${n.toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
}

function main() {
  if (!fs.existsSync(TMP)) {
    console.error(`找不到 ${TMP}`);
    process.exit(1);
  }

  const keepNames = new Set();
  for (const pattern of KEEP) {
    const re = globToRegex(pattern);
    for (const name of fs.readdirSync(TMP)) {
      if (re.test(name)) keepNames.add(name);
    }
  }

  const hits = [];
  for (const name of fs.readdirSync(TMP)) {
    if (name.startsWith('.')) continue;        // 隐藏的一律不碰
    if (keepNames.has(name)) continue;
    const full = path.join(TMP, name);
    hits.push({ name, full, size: WITH_SIZE ? sizeOf(full) : 0 });
  }

  hits.sort((a, b) => (WITH_SIZE ? b.size - a.size : a.name.localeCompare(b.name)));

  let total = 0;
  for (const hit of hits) {
    total += hit.size;
    console.log(`${DRY ? '[dry] ' : ''}${(WITH_SIZE ? human(hit.size) : '').padStart(10)}  ${hit.name}`);
  }

  if (hits.length === 0) {
    console.log('没有要清的（KEEP 之外是空的）。');
  } else {
    console.log(`\n共 ${hits.length} 项${WITH_SIZE ? `，${human(total)}` : ''}`);
  }

  if (hits.length === 0 || DRY) {
    if (DRY) console.log('（--dry：什么都没删）');
  } else {
    let removed = 0;
    for (const hit of hits) {
      try {
        rm(hit.full);
        removed++;
      } catch (err) {
        console.error(`删不掉 ${hit.name}：${err.message}`);
      }
    }

    console.log(`已清 ${removed}/${hits.length} 项${WITH_SIZE ? `，释放约 ${human(total)}` : ''}。`);
  }

  console.log(`\n保留：${[...keepNames].sort().join('、')}`);
}

main();
