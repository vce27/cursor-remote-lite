#!/usr/bin/env node
// Prints JSON { link, minutes, base } for the desktop shell QR.
import { createPairing, appBase, saveState, readState } from '../auth.mjs';

const base = appBase() || readState().relayUrl;
if (!base) {
  console.error(JSON.stringify({ error: '还没有公网地址：先在桌面软件里填写 relayUrl 并启动中继' }));
  process.exit(1);
}
const normalized = base.replace(/\/?$/, '/');
if (!readState().relayUrl) saveState({ relayUrl: normalized });
const { link, minutes } = createPairing(normalized);
process.stdout.write(JSON.stringify({ link, minutes, base: normalized }) + '\n');
