#!/usr/bin/env node
// Prints JSON { link, minutes, base } for the desktop shell QR.
import { createPairing, appBase, saveState, readState } from './auth.mjs';

const base = appBase() || readState().relayUrl;
if (!base) {
  console.error(JSON.stringify({ error: '????????????????? relayUrl ?????' }));
  process.exit(1);
}
const normalized = base.replace(/\/?$/, '/');
if (!readState().relayUrl) saveState({ relayUrl: normalized });
const { link, minutes } = createPairing(normalized);
process.stdout.write(JSON.stringify({ link, minutes, base: normalized }) + '\n');
