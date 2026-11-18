"use strict";
const api = globalThis.whale || globalThis.chrome;
const HOST = "com.mycelview.tabs";
let port = null, ready = false, pending = null, publishing = false, again = false;
const icons = new Map();
function status(text) { api.action.setTitle({title:"Mycelview 탭 연결 · "+text}); }
function schedule() { clearTimeout(pending); pending=setTimeout(publish,150); }
async function iconFor(tab) {
  if(!tab.url || !/^https?:/i.test(tab.url)) return "";
  const key=tab.favIconUrl||new URL(tab.url).origin;
  if(icons.has(key)) return icons.get(key);
  try {
    const url=new URL(api.runtime.getURL("/_favicon/"));url.searchParams.set("pageUrl",tab.url);url.searchParams.set("size","16");
    const response=await fetch(url.href);if(!response.ok)return "";
    const bytes=new Uint8Array(await response.arrayBuffer());if(bytes.length>8192)return "";
    let binary="";for(const b of bytes)binary+=String.fromCharCode(b);
    const image=btoa(binary);icons.set(key,image);
    if(icons.size>256)icons.delete(icons.keys().next().value);
    return image;
  } catch { return ""; }
}
async function publish() {
  if(!port||!ready)return;
  if(publishing){again=true;return;}
  publishing=true;
  try {
    const all=await api.windows.getAll({populate:true,windowTypes:["normal","popup"]});
    const windows=[], imageData={};let count=0;
    for(const win of all.slice(0,128)){
      if(win.incognito)continue;
      const tabs=[];
      for(const tab of win.tabs||[]){
        if(count++>=2000)break;
        let image=await iconFor(tab),icon="";
        if(image){icon=String(Object.keys(imageData).length);const known=Object.entries(imageData).find(([,v])=>v===image);if(known)icon=known[0];else if(Object.keys(imageData).length<64)imageData[icon]=image;else icon="";}
        tabs.push({id:tab.id,title:(tab.title||"제목 없는 탭").slice(0,512),active:!!tab.active,icon});
      }
      windows.push({id:win.id,focused:!!win.focused,tabs});
    }
    if(port&&ready)port.postMessage({type:"snapshot",windows,icons:imageData});
  } catch { status("연결 확인 필요"); }
  finally {publishing=false;if(again){again=false;schedule();}}
}
function connect(){
  if(port)return;
  try{
    const current=api.runtime.connectNative(HOST);port=current;ready=false;
    current.onMessage.addListener(async message=>{
      if(message.type==="ready"){ready=true;status("연결됨");schedule();return;}
      if(message.type!=="activate"||!Number.isInteger(message.tabId)||!Number.isInteger(message.windowId))return;
      try{
        const tab=await api.tabs.get(message.tabId);
        if(tab.windowId!==message.windowId||tab.incognito)throw new Error("stale tab");
        await api.tabs.update(message.tabId,{active:true});
        const win=await api.windows.get(message.windowId);
        if(win.state==="minimized")await api.windows.update(message.windowId,{state:"normal"});
        await api.windows.update(message.windowId,{focused:true});
        current.postMessage({type:"result",requestId:message.requestId,ok:true});schedule();
      }catch{
        try{current.postMessage({type:"result",requestId:message.requestId,ok:false});}catch{}
      }
    });
    current.onDisconnect.addListener(()=>{
      void api.runtime.lastError;if(port===current){port=null;ready=false;status("Mycelview 실행 또는 연결 설치 필요");}
    });
  }catch{port=null;ready=false;status("연결 설치 필요");}
}
for(const event of [api.tabs.onCreated,api.tabs.onRemoved,api.tabs.onActivated,api.tabs.onMoved,api.tabs.onAttached,api.tabs.onDetached,api.windows.onCreated,api.windows.onRemoved,api.windows.onFocusChanged])event.addListener(schedule);
api.tabs.onUpdated.addListener((_id,change)=>{if(change.title!==undefined||change.favIconUrl!==undefined||change.status==="complete")schedule();});
api.runtime.onStartup.addListener(connect);api.runtime.onInstalled.addListener(connect);
api.alarms.create("mycelview-reconnect",{periodInMinutes:1});
api.alarms.onAlarm.addListener(()=>{if(!port)connect();else schedule();});
api.runtime.onMessage.addListener((message,_sender,reply)=>{
  if(message.type==="reconnect"){if(port){port.disconnect();port=null;}connect();reply({ready});}
  if(message.type==="status")reply({ready,connected:!!port});
});
connect();