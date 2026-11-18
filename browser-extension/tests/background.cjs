const fs=require("fs"),vm=require("vm"),assert=require("assert");
const event=()=>({listeners:[],addListener(fn){this.listeners.push(fn)}});
(async()=>{
 for(const namespace of ["chrome","whale"]){
  let sent=[],updates=[],listeners={},timers=[];
  const port={onMessage:event(),onDisconnect:event(),postMessage:m=>sent.push(m),disconnect(){}};
  const api={action:{setTitle(){}},runtime:{connectNative:()=>port,getURL:p=>"chrome-extension://test"+p,onStartup:event(),onInstalled:event(),onMessage:event()},alarms:{create(){},onAlarm:event()},
   tabs:{get:async id=>({id,windowId:7,incognito:false}),update:async(id,p)=>updates.push(["tab",id,p])},
   windows:{getAll:async()=>[{id:7,focused:true,tabs:[{id:9,title:"탭",active:true,url:"https://example.com"}]},{id:8,incognito:true,tabs:[{id:10,title:"private"}]}],get:async()=>({state:"minimized"}),update:async(id,p)=>updates.push(["window",id,p])}};
  for(const name of ["onCreated","onRemoved","onActivated","onMoved","onAttached","onDetached","onUpdated"])api.tabs[name]=event();
  for(const name of ["onCreated","onRemoved","onFocusChanged"])api.windows[name]=event();
  const context=vm.createContext({[namespace]:api,URL,Uint8Array,Map,fetch:async()=>({ok:false}),btoa:s=>Buffer.from(s,"binary").toString("base64"),setTimeout:fn=>(timers.push(fn),timers.length),clearTimeout(){}});
  vm.runInContext(fs.readFileSync("browser-extension/background.js","utf8"),context);
  await port.onMessage.listeners[0]({type:"ready"});await timers.shift()();
  const snap=sent.find(m=>m.type==="snapshot");assert.equal(snap.windows.length,1);assert.equal(snap.windows[0].tabs[0].title,"탭");assert(!JSON.stringify(snap).includes("https:"));
  await port.onMessage.listeners[0]({type:"activate",tabId:9,windowId:7,requestId:"ok"});
  assert.equal(updates.length,3);assert(sent.some(m=>m.requestId==="ok"&&m.ok));
  updates=[];await port.onMessage.listeners[0]({type:"activate",tabId:9,windowId:999,requestId:"stale"});
  assert.equal(updates.length,0);assert(sent.some(m=>m.requestId==="stale"&&!m.ok));
 }
 console.log("PASS: Chrome/Edge and Whale API routing; snapshot privacy; tab activation; minimized restore; stale-window rejection");
})().catch(e=>{console.error(e);process.exitCode=1});