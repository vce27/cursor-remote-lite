<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from "vue";
import { invoke } from "@tauri-apps/api/core";
import QRCode from "qrcode";

type Status = {
  relay_running: boolean;
  relay_listening: boolean;
  cdp_ports: number[];
  relay_url: string;
  repo_root: string;
  port: number;
};

const status = ref<Status | null>(null);
const relayUrl = ref("");
const busy = ref(false);
const toast = ref("");
const toastType = ref<"info" | "success" | "error">("info");
const pairLink = ref("");
const pairMinutes = ref(0);
const qrDataUrl = ref("");
let timer: number | undefined;

async function refresh() {
  try {
    status.value = await invoke<Status>("get_status");
    if (!relayUrl.value && status.value.relay_url) {
      relayUrl.value = status.value.relay_url;
    }
  } catch (e) {
    showToast(String(e), "error");
  }
}

function showToast(msg: string, type: "info" | "success" | "error" = "info") {
  toast.value = msg;
  toastType.value = type;
  window.setTimeout(() => {
    if (toast.value === msg) toast.value = "";
  }, type === "success" ? 1600 : 3000);
}

async function run(fn: () => Promise<string>, okType: "success" | "info" = "success") {
  if (busy.value) return;
  busy.value = true;
  try {
    const msg = await fn();
    showToast(msg, okType);
    await refresh();
  } catch (e) {
    showToast(String(e), "error");
  } finally {
    busy.value = false;
  }
}

const saveUrl = () =>
  run(async () => {
    await invoke("save_relay_url", { url: relayUrl.value.trim() });
    return "已保存公网地址";
  });

const start = () => run(() => invoke<string>("start_relay"));
const stop = () => run(() => invoke<string>("stop_relay"));
const ensureCursor = () => run(() => invoke<string>("ensure_cursor"));

async function makePair() {
  await run(async () => {
    const info = await invoke<{ link: string; minutes: number; base: string }>("create_pair_link");
    pairLink.value = info.link;
    pairMinutes.value = info.minutes;
    qrDataUrl.value = await QRCode.toDataURL(info.link, { width: 240, margin: 1 });
    return `配对码已生成（${info.minutes} 分钟内有效）`;
  });
}

async function copyLink() {
  if (!pairLink.value) return;
  try {
    await navigator.clipboard.writeText(pairLink.value);
    showToast("已复制配对链接", "success");
  } catch {
    showToast("复制失败，请手动选中链接", "error");
  }
}

const stateLabel = computed(() => {
  if (!status.value) return "读取中…";
  if (status.value.relay_listening) return "中继在线";
  if (status.value.relay_running) return "启动中…";
  return "中继已停止";
});

const stateClass = computed(() => {
  if (!status.value) return "";
  if (status.value.relay_listening) return "ok";
  if (status.value.relay_running) return "warn";
  return "off";
});

onMounted(() => {
  refresh();
  timer = window.setInterval(refresh, 2500);
});
onUnmounted(() => {
  if (timer) clearInterval(timer);
});
</script>

<template>
  <div class="page">
    <header class="head">
      <div>
        <h1>Cursor Remote Lite</h1>
        <p class="desc">电脑端托盘软件 · 手机用 Safari 扫码即可（无需安装 App）</p>
      </div>
      <div class="badge" :class="stateClass">
        <i></i>{{ stateLabel }}
      </div>
    </header>

    <section class="card">
      <div class="block">
        <div class="block-title">
          <div>
            <div class="t">公网地址（ChmlFrp / 隧道）</div>
            <div class="s">http:// 也可：配对会打开中继本身，避免混合内容</div>
          </div>
        </div>
        <div class="fields">
          <label>
            <span>relayUrl</span>
            <input v-model="relayUrl" placeholder="http://你的隧道:端口/" />
          </label>
        </div>
        <div class="foot">
          <button class="ghost" :disabled="busy" @click="saveUrl">保存</button>
        </div>
      </div>

      <div class="block">
        <div class="block-title">
          <div>
            <div class="t">运行状态</div>
            <div class="s">
              监听端口 {{ status?.port ?? 3939 }}
              · Cursor CDP
              {{ status?.cdp_ports?.length ? status.cdp_ports.map((p) => ":" + p).join(" ") : "未检测到" }}
            </div>
          </div>
        </div>
        <div class="foot">
          <button class="ghost" :disabled="busy" @click="ensureCursor">启动 / 检查 Cursor</button>
          <button class="ghost" :disabled="busy" @click="stop">停止中继</button>
          <button class="primary" :disabled="busy" @click="start">一键启动中继</button>
        </div>
      </div>

      <div class="block last">
        <div class="block-title">
          <div>
            <div class="t">配对 iPhone</div>
            <div class="s">生成二维码后用相机扫码 → Safari → 分享 → 添加到主屏幕</div>
          </div>
        </div>
        <div class="pair">
          <img v-if="qrDataUrl" :src="qrDataUrl" alt="pair qr" />
          <div v-else class="qr-ph">点击下方生成配对码</div>
          <div v-if="pairLink" class="link">
            <code>{{ pairLink }}</code>
            <button class="ghost" @click="copyLink">复制链接</button>
          </div>
          <p v-if="pairMinutes" class="hint">{{ pairMinutes }} 分钟内有效，只能用一次</p>
        </div>
        <div class="foot">
          <button class="primary" :disabled="busy" @click="makePair">生成配对二维码</button>
        </div>
      </div>
    </section>

    <p class="repo">仓库：{{ status?.repo_root || "…" }}</p>

    <div v-if="toast" class="toast" :class="toastType">{{ toast }}</div>
  </div>
</template>

<style>
:root {
  --erp-primary: #007aff;
  --erp-primary-hover: #1677ff;
  --erp-border: #d9d9d9;
  --erp-border-light: #f0f0f0;
  --erp-text: rgba(0, 0, 0, 0.88);
  --erp-text-secondary: rgba(0, 0, 0, 0.45);
  --erp-text-muted: #697386;
  --erp-success: #34c759;
  --erp-danger: #ff4d4f;
  --erp-radius: 8px;
  --erp-radius-sm: 6px;
  --erp-shadow: 0 6px 16px rgba(0, 0, 0, 0.08), 0 3px 6px -4px rgba(0, 0, 0, 0.12),
    0 9px 28px 8px rgba(0, 0, 0, 0.05);
  --erp-focus: rgba(5, 145, 255, 0.1);
  font-family: -apple-system, BlinkMacSystemFont, "PingFang SC", "Microsoft YaHei", sans-serif;
  font-size: 14px;
  line-height: 22px;
  color: var(--erp-text);
  background: #f5f5f5;
}
* { box-sizing: border-box; }
body { margin: 0; }
.page { padding: 20px 24px 28px; max-width: 560px; margin: 0 auto; }
.head { display: flex; align-items: flex-start; justify-content: space-between; gap: 16px; margin-bottom: 16px; }
h1 { margin: 0; font-size: 16px; font-weight: 600; line-height: 24px; }
.desc { margin: 0; font-size: 12px; color: var(--erp-text-secondary); }
.badge {
  display: inline-flex; align-items: center; gap: 6px; height: 28px; padding: 0 10px;
  border: 1px solid var(--erp-border); border-radius: 999px; background: #fff; font-size: 12px; flex: none;
}
.badge i { width: 8px; height: 8px; border-radius: 50%; background: #bbb; }
.badge.ok { color: #135200; border-color: #b7eb8f; background: #f6ffed; }
.badge.ok i { background: var(--erp-success); }
.badge.warn { color: #ad6800; border-color: #ffe58f; background: #fffbe6; }
.badge.warn i { background: #faad14; }
.badge.off i { background: var(--erp-danger); }
.card {
  background: #fff; border: 1px solid var(--erp-border-light); border-radius: var(--erp-radius);
  box-shadow: var(--erp-shadow); overflow: hidden;
}
.block { padding: 12px 0; margin: 0 24px; border-bottom: 1px solid var(--erp-border-light); }
.block.last { border-bottom: none; }
.block-title { display: flex; align-items: flex-start; justify-content: space-between; gap: 16px; margin-bottom: 8px; }
.t { font-size: 14px; font-weight: 500; line-height: 22px; }
.s { font-size: 12px; color: var(--erp-text-muted); margin-top: 2px; }
.fields { padding-left: 12px; }
label { display: flex; flex-direction: column; gap: 8px; font-size: 14px; }
label span { padding-bottom: 0; }
input {
  width: 100%; max-width: 100%; height: 32px; padding: 4px 11px;
  border: 1px solid var(--erp-border); border-radius: var(--erp-radius-sm); font: inherit;
}
input:focus { outline: none; border-color: var(--erp-primary-hover); box-shadow: 0 0 0 2px var(--erp-focus); }
.foot { display: flex; justify-content: flex-end; gap: 8px; margin-top: 12px; flex-wrap: wrap; }
button {
  height: 32px; padding: 0 15px; border-radius: var(--erp-radius-sm); font-size: 14px; font-weight: 400;
  border: 1px solid var(--erp-border); background: #fff; color: var(--erp-text); cursor: pointer;
}
button:disabled { opacity: 0.45; cursor: default; }
button.ghost:hover:not(:disabled) { color: var(--erp-primary-hover); border-color: var(--erp-primary-hover); }
button.primary { background: var(--erp-primary); border-color: var(--erp-primary); color: #fff; }
.pair { padding-left: 12px; }
.pair img { width: 240px; height: 240px; border: 1px solid var(--erp-border-light); border-radius: var(--erp-radius); background: #fff; }
.qr-ph {
  width: 240px; height: 160px; display: flex; align-items: center; justify-content: center;
  border: 1px dashed var(--erp-border); border-radius: var(--erp-radius); color: var(--erp-text-secondary); font-size: 12px;
}
.link { display: flex; gap: 8px; align-items: flex-start; margin-top: 10px; }
.link code {
  flex: 1; min-width: 0; font-size: 12px; word-break: break-all; background: #fafafa;
  border: 1px solid var(--erp-border-light); border-radius: 6px; padding: 8px 10px;
}
.hint { font-size: 12px; color: var(--erp-text-secondary); margin: 8px 0 0; }
.repo { margin: 12px 0 0; font-size: 12px; color: var(--erp-text-muted); word-break: break-all; }
.toast {
  position: fixed; top: 16px; left: 50%; transform: translateX(-50%);
  max-width: min(280px, calc(100vw - 32px)); background: #fff; border: 1px solid var(--erp-border-light);
  border-radius: var(--erp-radius); padding: 8px 12px; font-size: 13px; font-weight: 510;
  box-shadow: var(--erp-shadow); z-index: 50;
}
.toast.success { box-shadow: var(--erp-shadow); }
.toast.error { color: var(--erp-danger); }
</style>
