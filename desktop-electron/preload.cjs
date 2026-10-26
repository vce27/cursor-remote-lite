const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('cr', {
  getStatus: () => ipcRenderer.invoke('get_status'),
  saveRelayUrl: url => ipcRenderer.invoke('save_relay_url', url),
  startRelay: () => ipcRenderer.invoke('start_relay'),
  stopRelay: () => ipcRenderer.invoke('stop_relay'),
  ensureCursor: () => ipcRenderer.invoke('ensure_cursor'),
  createPairLink: () => ipcRenderer.invoke('create_pair_link'),
  openExternal: url => ipcRenderer.invoke('open_external', url),
});
