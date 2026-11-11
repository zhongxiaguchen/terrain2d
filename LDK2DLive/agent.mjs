#!/usr/bin/env node
/**
 * LDK2D 本地协作代理 —— 把「事件流 → 判断 → 补丁」这条闭环真正转起来。
 *
 * 它是什么
 * --------
 * 一个**规则型**代理：盯着 `events.jsonl`，看到人刚把地形改完（`settle`），
 * 就读 `state.json` 把高度数组拿下来，跑一遍「最大可行坡角」约束，
 * 算出一段补丁写回 `commands.jsonl`。
 *
 * 它不是大模型。它是这条通道的**参考实现**：证明"编辑器能实时把现场推出来、
 * 外部能实时把改动打回去"这件事成立。真接大模型时，替换掉 `decide()` 一个函数就行。
 *
 * 为什么要有它
 * ------------
 * 真正的 AI（对话里的那个）受对话轮次限制，一轮只能动一次。要看到"毫秒级来回"，
 * 必须有一个常驻进程。这就是那个进程。
 *
 * 用法
 * ----
 *   node LDK2DLive/agent.mjs            # 前台跑，日志直接看
 *   start-agent.bat                     # 同上，双击就行
 *
 * 环境变量：
 *   LDK2D_MAX_SLOPE=2.0    最大坡度（垂直/水平）。2.0 ≈ 63°，超过就削
 *   LDK2D_COOLDOWN=400     同一块地形的冷却（毫秒），防止和 AI 自己打架
 *   LDK2D_DRY=1            只算不发，用来观察它会怎么改
 */

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const DIR = path.dirname(fileURLToPath(import.meta.url));

const EVENTS = path.join(DIR, 'events.jsonl');
const STATE = path.join(DIR, 'state.json');
const COMMANDS = path.join(DIR, 'commands.jsonl');
const CURSOR = path.join(DIR, 'agent-cursor.json');
const LOG = path.join(DIR, 'agent.log');

const MAX_SLOPE = Number(process.env.LDK2D_MAX_SLOPE ?? 2.0);
const COOLDOWN_MS = Number(process.env.LDK2D_COOLDOWN ?? 400);
const DRY = process.env.LDK2D_DRY === '1';

// 迭代上限。坡度限制是逐次收敛的，一次削不平，多跑几轮。
const MAX_ITER = 60;
// 改动小于这个就不发了 —— 免得为了 0.001 的差异来回刷命令。
const EPS = 0.004;

// ------------------------------------------------------------------ 日志

function log(...args) {
  const line = `[${new Date().toTimeString().slice(0, 8)}] ${args.join(' ')}`;
  console.log(line);
  try {
    fs.appendFileSync(LOG, line + '\n');
  } catch {
    /* 日志写不进去不该影响主流程 */
  }
}

// ------------------------------------------------------------------ 游标

function loadCursor() {
  try {
    const j = JSON.parse(fs.readFileSync(CURSOR, 'utf8'));
    return Number(j.offset) || 0;
  } catch {
    return 0;
  }
}

function saveCursor(offset) {
  try {
    fs.writeFileSync(CURSOR, JSON.stringify({ offset }));
  } catch {
    /* ignore */
  }
}

// ------------------------------------------------------------------ 发命令

let seq = 0;

function send(cmd) {
  const full = { id: `agent-${Date.now().toString(36)}-${++seq}`, ...cmd };
  const line = JSON.stringify(full) + '\n';

  if (DRY) {
    log(`[dry] ${line.trim()}`);
    return full.id;
  }

  try {
    fs.appendFileSync(COMMANDS, line);
  } catch (e) {
    log(`写入 commands.jsonl 失败：${e.message}`);
    return null;
  }

  return full.id;
}

// ------------------------------------------------------------------ 策略

/**
 * 核心：把人改出来的地形，压到「最大可行坡角」以内。
 *
 * 这不是平滑 —— 平滑会把整个地形糊掉。这是**坡度约束**：
 * 只搬动超限的那一点点土，其他地方一个数都不碰。相当于"把太陡的地方
 * 削给旁边的低处"，土方总量基本守恒。
 *
 * 返回 null 表示"这块地形没问题，不用动"。
 */
function decide(terrain) {
  const h = terrain.heights;
  const xs = terrain.topX;

  if (!Array.isArray(h) || !Array.isArray(xs) || h.length < 3) return null;

  const n = h.length;

  // 双缓冲：读 cur、写 next，一轮结束再交换。
  //
  // 直接就地改（读和写同一个数组）会**偏向扫描方向** —— 从左往右扫，
  // 左边的坡会先被削到位，右边的还差一大截，削出来是个歪的斜坡。
  // 分成两个数组就没有这个问题，左右对称。
  let cur = Float64Array.from(h);
  let next = Float64Array.from(h);

  let touchedFrom = Infinity;
  let touchedTo = -Infinity;
  let worst = 0;

  for (let iter = 0; iter < MAX_ITER; iter++) {
    let changed = false;
    next.set(cur);

    for (let i = 0; i < n - 1; i++) {
      const dx = Math.abs(xs[i + 1] - xs[i]);
      if (dx < 1e-5) continue;

      const dy = cur[i + 1] - cur[i];
      const slope = Math.abs(dy) / dx;
      if (slope <= MAX_SLOPE) continue;

      if (slope > worst) worst = slope;

      // 超出的那部分，两端各让一半。方向由 dy 的符号决定，
      // 所以陡坡会被"推平"，而不是整体抬高或压低。
      const maxDy = MAX_SLOPE * dx;
      const excess = (Math.abs(dy) - maxDy) * 0.5;
      const sign = dy > 0 ? 1 : -1;

      next[i] += sign * excess;
      next[i + 1] -= sign * excess;

      if (i < touchedFrom) touchedFrom = i;
      if (i + 1 > touchedTo) touchedTo = i + 1;

      changed = true;
    }

    const swap = cur;
    cur = next;
    next = swap;

    if (!changed) break;
  }

  if (touchedTo < touchedFrom) return null;
  if (worst <= MAX_SLOPE) return null;

  // 只发改动的区间，两端各外扩 1 个点，避免接缝处出现新的折角。
  const from = Math.max(0, touchedFrom - 1);
  const to = Math.min(n - 1, touchedTo + 1);

  const values = [];
  let maxDelta = 0;

  for (let i = from; i <= to; i++) {
    const v = Number(cur[i].toFixed(4));
    values.push(v);
    const d = Math.abs(v - h[i]);
    if (d > maxDelta) maxDelta = d;
  }

  if (maxDelta < EPS) return null;

  return { from, to, values, maxDelta, worst };
}

// ------------------------------------------------------------------ 处理事件

const lastActed = new Map();

function onSettle(ev) {
  const target = ev.target;
  if (!target) return;

  // 自己刚发的补丁不算 —— 否则会陷入"改 → 事件 → 再改"的自激振荡。
  if (ev.src === 'ai') return;

  const now = Date.now();
  const last = lastActed.get(target) ?? 0;
  if (now - last < COOLDOWN_MS) return;

  let state;
  try {
    state = JSON.parse(fs.readFileSync(STATE, 'utf8'));
  } catch (e) {
    log(`读 state.json 失败：${e.message}`);
    return;
  }

  const terrain = (state.terrains ?? []).find((t) => t.id === target);
  if (!terrain) return;

  // 人还在拖 → 让位。这条和编辑器侧的判断是一致的，双保险。
  if (terrain.interactive) {
    log(`${target} 仍在交互中，跳过`);
    return;
  }

  const patch = decide(terrain);
  if (!patch) return;

  const id = send({
    op: 'patch',
    target,
    start: patch.from,
    values: patch.values,
    note: `坡度 ${patch.worst.toFixed(2)} > ${MAX_SLOPE}，削平 ${patch.from}~${patch.to}`,
  });

  if (!id) return;

  lastActed.set(target, now);

  log(
    `${target} 坡度超限（最陡 ${patch.worst.toFixed(2)}，上限 ${MAX_SLOPE}）` +
      ` → 补丁 [${patch.from}..${patch.to}]，最大改动 ${patch.maxDelta.toFixed(3)}`
  );
}

// ------------------------------------------------------------------ 主循环

function tick() {
  let size;
  try {
    size = fs.statSync(EVENTS).size;
  } catch {
    return; // Unity 还没起来
  }

  let offset = loadCursor();

  // 事件流被轮转过 / 重建过 —— 从头读。
  if (size < offset) offset = 0;

  if (size === offset) return;

  let text;
  try {
    const fd = fs.openSync(EVENTS, 'r');
    const buf = Buffer.alloc(size - offset);
    fs.readSync(fd, buf, 0, buf.length, offset);
    fs.closeSync(fd);
    text = buf.toString('utf8');
  } catch (e) {
    log(`读事件流失败：${e.message}`);
    return;
  }

  const lastNl = text.lastIndexOf('\n');
  if (lastNl < 0) return; // 半个事件，等下一次

  const block = text.slice(0, lastNl);

  // 游标是**字节**偏移，不是字符偏移 —— 中文名占 3 字节，
  // 用 lastNl 直接加会越读越歪，最后把整行 JSON 从中间切开。
  saveCursor(offset + Buffer.byteLength(text.slice(0, lastNl + 1), 'utf8'));

  for (const line of block.split('\n')) {
    const t = line.trim();
    if (!t) continue;

    let ev;
    try {
      ev = JSON.parse(t);
    } catch {
      continue;
    }

    if (ev.kind === 'session') {
      log(`编辑器会话已连接（Unity ${ev.unity ?? '?'}，play=${ev.play}）`);
      continue;
    }

    if (ev.kind === 'error') {
      log(`编辑器报错：${ev.message}`);
      continue;
    }

    if (ev.kind === 'settle') onSettle(ev);
  }
}

log(`协作代理启动  最大坡度 ${MAX_SLOPE}  冷却 ${COOLDOWN_MS}ms${DRY ? '  [dry]' : ''}`);
log(`监听 ${EVENTS}`);
log(`等待 Unity 打开（打开后会自动连上，不用重启本程序）`);

setInterval(tick, 50);

// Ctrl+C 正常退出
process.on('SIGINT', () => {
  log('退出');
  process.exit(0);
});
