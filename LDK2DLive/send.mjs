#!/usr/bin/env node
/**
 * 往 Unity 发一条命令（或一批）。给"人"和"对话里的 AI"用的最小工具。
 *
 *   node LDK2DLive/send.mjs '{"op":"patch","target":"/地形_A","start":40,"values":[1,2,3]}'
 *   node LDK2DLive/send.mjs cmd.json          # 从文件读，可以是一个数组 = 一批
 *   node LDK2DLive/send.mjs --tail 20         # 看最近 20 条事件
 *
 * 发完不用等，编辑器 16ms 内会来取。要看结果就跑 --tail。
 */

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const DIR = path.dirname(fileURLToPath(import.meta.url));
const COMMANDS = path.join(DIR, 'commands.jsonl');
const EVENTS = path.join(DIR, 'events.jsonl');
const STATE = path.join(DIR, 'state.json');
const HEARTBEAT = path.join(DIR, 'heartbeat.json');

const argv = process.argv.slice(2);

if (argv.length === 0) {
  console.log(`用法：
  node send.mjs '<JSON>'     发一条命令
  node send.mjs <文件>       从文件读（数组 = 一批）
  node send.mjs --tail [n]   看最近 n 条事件（默认 20）
  node send.mjs --state      打印当前地形快照（截断高度数组）
  node send.mjs --ping       看编辑器活着没有`);
  process.exit(0);
}

if (argv[0] === '--tail') {
  const n = Number(argv[1] ?? 20);
  try {
    const lines = fs.readFileSync(EVENTS, 'utf8').trim().split('\n').filter(Boolean);
    for (const line of lines.slice(-n)) console.log(line);
  } catch {
    console.log('还没有 events.jsonl —— Unity 可能没打开。');
  }
  process.exit(0);
}

if (argv[0] === '--ping') {
  try {
    console.log(fs.readFileSync(HEARTBEAT, 'utf8').trim());
  } catch {
    console.log('没有 heartbeat.json —— Unity 没打开，或实时通道没跑起来。');
  }
  process.exit(0);
}

if (argv[0] === '--state') {
  try {
    const s = JSON.parse(fs.readFileSync(STATE, 'utf8'));
    for (const t of s.terrains ?? []) {
      const h = t.heights ?? [];
      console.log(
        `${t.id}  采样 ${t.samples}  间距 ${t.spacing}  ` +
          `Y ${Math.min(...h).toFixed(2)}~${Math.max(...h).toFixed(2)}  rev ${t.revision}`
      );
    }
    for (const a of s.actors ?? []) {
      console.log(`${a.id}  [${a.actorKind}/${a.type}]  @ ${a.position.map((v) => v.toFixed(2)).join(',')}`);
    }
    if (!(s.terrains ?? []).length) console.log('场景里没有地形。');
  } catch (e) {
    console.log('读不到 state.json：' + e.message);
  }
  process.exit(0);
}

// ---- 发命令 ----

let payload = argv[0];

if (fs.existsSync(payload)) payload = fs.readFileSync(payload, 'utf8');

let parsed;
try {
  parsed = JSON.parse(payload);
} catch (e) {
  console.error('不是合法 JSON：' + e.message);
  process.exit(1);
}

const list = Array.isArray(parsed) ? parsed : [parsed];
const stamp = Date.now().toString(36);

const lines = list.map((c, i) => {
  const cmd = { id: `cli-${stamp}-${i}`, ...c };
  return JSON.stringify(cmd);
});

fs.appendFileSync(COMMANDS, lines.join('\n') + '\n');

console.log(`已发送 ${lines.length} 条命令。`);
for (const l of lines) console.log('  ' + l);
