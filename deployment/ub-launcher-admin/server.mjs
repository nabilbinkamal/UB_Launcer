import { createServer } from 'node:http';
import { mkdir, readFile, rename, stat, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { extname, join, normalize } from 'node:path';

const port = Number(process.env.PORT || 8602);
const password = process.env.UB_ADMIN_PASSWORD;
const publicBaseUrl = (process.env.UB_PUBLIC_BASE_URL || 'https://launcher.unitedbangla.top').replace(/\/$/, '');
const dataDirectory = process.env.UB_ADMIN_DATA_DIR || join(process.cwd(), 'data');
const assetDirectory = join(dataDirectory, 'assets');
const configPath = join(dataDirectory, 'config.json');
const maximumBodyBytes = 6 * 1024 * 1024;

if (!password || password.length < 12) {
  throw new Error('Set UB_ADMIN_PASSWORD to a unique password with at least 12 characters.');
}

const defaultConfig = {
  schemaVersion: 1,
  latestVersion: '1.0.0',
  minimumVersion: '1.0.0',
  forceUpdate: false,
  updateTitle: 'A new UB Launcher update is ready',
  updateMessage: 'Update now to get the latest fixes and improvements.',
  updateUrl: '',
  assets: {}
};

await mkdir(assetDirectory, { recursive: true });
if (!existsSync(configPath)) await saveConfig(defaultConfig);

const server = createServer(async (request, response) => {
  try {
    const url = new URL(request.url || '/', `http://${request.headers.host || 'localhost'}`);

    if (request.method === 'GET' && url.pathname === '/config.json') {
      return json(response, 200, await loadConfig(), { 'Cache-Control': 'no-cache' });
    }

    if (request.method === 'GET' && url.pathname.startsWith('/assets/')) {
      return serveAsset(response, url.pathname.slice('/assets/'.length));
    }

    if (!isAuthorized(request)) {
      response.writeHead(401, { 'WWW-Authenticate': 'Basic realm="UB Launcher Admin"' });
      return response.end('Admin login required.');
    }

    if (request.method === 'GET' && url.pathname === '/admin') {
      response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' });
      return response.end(adminHtml);
    }

    if (request.method === 'GET' && url.pathname === '/admin/config') {
      return json(response, 200, await loadConfig(), { 'Cache-Control': 'no-store' });
    }

    if (request.method === 'PUT' && url.pathname === '/admin/config') {
      const submitted = await readJson(request);
      const configuration = sanitizeConfig(submitted);
      await saveConfig(configuration);
      return json(response, 200, configuration);
    }

    if (request.method === 'POST' && url.pathname === '/admin/upload') {
      const uploaded = await readJson(request);
      const filename = safeFilename(uploaded.name);
      const encoded = String(uploaded.base64 || '').replace(/^data:[^;]+;base64,/, '');
      const bytes = Buffer.from(encoded, 'base64');
      if (!filename || !bytes.length || bytes.length > maximumBodyBytes) throw new Error('Choose a PNG, JPG, WEBP, or SVG file smaller than 6 MB.');
      const extension = extname(filename).toLowerCase();
      if (!['.png', '.jpg', '.jpeg', '.webp', '.svg'].includes(extension)) throw new Error('Only PNG, JPG, WEBP, and SVG files are allowed.');
      if (extension === '.svg' && !bytes.toString('utf8', 0, 256).toLowerCase().includes('<svg')) throw new Error('The SVG file is invalid.');
      await writeFile(join(assetDirectory, filename), bytes);
      return json(response, 200, { url: `${publicBaseUrl}/assets/${encodeURIComponent(filename)}` });
    }

    response.writeHead(404);
    response.end('Not found.');
  } catch (error) {
    json(response, 400, { error: error instanceof Error ? error.message : 'Request failed.' });
  }
});

server.listen(port, '127.0.0.1', () => console.log(`UB Launcher admin ready on 127.0.0.1:${port}`));

function isAuthorized(request) {
  const header = request.headers.authorization || '';
  const expected = `Basic ${Buffer.from(`admin:${password}`).toString('base64')}`;
  return header === expected;
}

async function readJson(request) {
  const chunks = [];
  let total = 0;
  for await (const chunk of request) {
    total += chunk.length;
    if (total > maximumBodyBytes) throw new Error('Request is too large.');
    chunks.push(chunk);
  }
  try { return JSON.parse(Buffer.concat(chunks).toString('utf8')); }
  catch { throw new Error('Invalid JSON request.'); }
}

async function loadConfig() {
  try { return sanitizeConfig(JSON.parse(await readFile(configPath, 'utf8'))); }
  catch { return { ...defaultConfig }; }
}

async function saveConfig(value) {
  const temporaryPath = `${configPath}.tmp`;
  await writeFile(temporaryPath, `${JSON.stringify(value, null, 2)}\n`, 'utf8');
  await rename(temporaryPath, configPath);
}

function sanitizeConfig(value) {
  const input = value && typeof value === 'object' ? value : {};
  const assets = input.assets && typeof input.assets === 'object' ? input.assets : {};
  const validAssets = Object.fromEntries(Object.entries(assets)
    .filter(([name, url]) => /^[a-zA-Z][a-zA-Z0-9]{0,63}$/.test(name) && isHttpsUrl(url))
    .slice(0, 40));
  return {
    schemaVersion: 1,
    latestVersion: text(input.latestVersion, 32, defaultConfig.latestVersion),
    minimumVersion: text(input.minimumVersion, 32, defaultConfig.minimumVersion),
    forceUpdate: Boolean(input.forceUpdate),
    updateTitle: text(input.updateTitle, 120, defaultConfig.updateTitle),
    updateMessage: text(input.updateMessage, 500, defaultConfig.updateMessage),
    updateUrl: isHttpsUrl(input.updateUrl) ? String(input.updateUrl).trim() : '',
    assets: validAssets
  };
}

function text(value, maximum, fallback) {
  const result = String(value || '').trim().slice(0, maximum);
  return result || fallback;
}

function isHttpsUrl(value) {
  try { return new URL(String(value)).protocol === 'https:'; }
  catch { return false; }
}

function safeFilename(value) {
  const basename = normalize(String(value || '')).replace(/^.*[\\/]/, '');
  return /^[a-zA-Z0-9][a-zA-Z0-9._-]{0,100}$/.test(basename) ? basename : '';
}

async function serveAsset(response, filename) {
  const safeName = safeFilename(filename);
  const filePath = safeName && join(assetDirectory, safeName);
  if (!filePath || !existsSync(filePath) || !(await stat(filePath)).isFile()) {
    response.writeHead(404); return response.end('Not found.');
  }
  const types = { '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.webp': 'image/webp', '.svg': 'image/svg+xml' };
  response.writeHead(200, { 'Content-Type': types[extname(filePath).toLowerCase()] || 'application/octet-stream', 'Cache-Control': 'public, max-age=300' });
  response.end(await readFile(filePath));
}

function json(response, status, value, headers = {}) {
  response.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', ...headers });
  response.end(JSON.stringify(value));
}

const adminHtml = `<!doctype html><html><head><meta charset="utf-8"><title>UB Launcher Admin</title><style>
*{box-sizing:border-box}body{margin:0;background:#07111b;color:#e9f5ff;font:15px system-ui,sans-serif}.wrap{max-width:900px;margin:36px auto;padding:0 20px}h1{margin-bottom:5px}p{color:#9bb3c8}section{margin:18px 0;padding:22px;background:#0d1d2b;border:1px solid #23465f;border-radius:12px}label{display:block;margin:14px 0 5px;font-weight:650}input,textarea{width:100%;padding:11px;border:1px solid #31546c;border-radius:7px;background:#07131e;color:white;font:inherit}textarea{min-height:86px}button{margin-top:18px;padding:11px 17px;border:1px solid #4cb9ef;border-radius:7px;background:#147cba;color:white;font-weight:700;cursor:pointer}.row{display:grid;grid-template-columns:1fr 1fr;gap:14px}.check{display:flex;gap:8px;align-items:center}.check input{width:auto}.asset{border-top:1px solid #203b50;margin-top:16px;padding-top:10px}.muted,#status{color:#8fb5cc;font-size:13px}</style></head><body><main class="wrap"><h1>UB Launcher Admin</h1><p>Save করলে launcher next time open হলে update popup ও branding বদলে যাবে।</p><form id="form"><section><h2>Update popup</h2><div class="row"><div><label>Latest version</label><input name="latestVersion" placeholder="1.2.0"></div><div><label>Minimum required version</label><input name="minimumVersion" placeholder="1.1.0"></div></div><label class="check"><input type="checkbox" name="forceUpdate"> বাধ্যতামূলক update (Later button থাকবে না)</label><label>Popup title</label><input name="updateTitle"><label>Popup message</label><textarea name="updateMessage"></textarea><label>Download / release URL (HTTPS)</label><input name="updateUrl" placeholder="https://..."></section><section><h2>Branding assets</h2><p class="muted">একটি file upload করলে public HTTPS URL automatically বসে যাবে। SVG শুধু menu/social icon-এর জন্য, PNG/JPG/WEBP banner/logo-এর জন্য।</p><div id="assets"></div></section><button>Publish changes</button><div id="status"></div></form></main><script>
const fields=[['heroBanner','Home banner','image'],['logo','UB logo','image'],['homeIcon','Home menu icon','svg'],['instanceIcon','Instance menu icon','svg'],['modsIcon','Content menu icon','svg'],['customizeIcon','Customize menu icon','svg'],['performanceIcon','Performance menu icon','svg'],['serverIcon','Create server menu icon','svg'],['settingsIcon','Settings menu icon','svg'],['discordIcon','Discord icon','svg'],['tiktokIcon','TikTok icon','svg'],['youtubeIcon','YouTube icon','svg'],['webIcon','Website icon','svg']];let config={assets:{}};const assets=document.querySelector('#assets');assets.innerHTML=fields.map(([key,label,kind])=>\`<div class="asset"><label>\${label}</label><input data-asset="\${key}" placeholder="https://..."> <input type="file" data-upload="\${key}" accept="\${kind==='svg'?'.svg':'image/png,image/jpeg,image/webp'}"></div>\`).join('');
async function load(){config=await (await fetch('/admin/config')).json();for(const [key] of fields)document.querySelector('[data-asset="'+key+'"]').value=config.assets[key]||'';for(const name of ['latestVersion','minimumVersion','updateTitle','updateMessage','updateUrl'])form.elements[name].value=config[name]||'';form.elements.forceUpdate.checked=!!config.forceUpdate}load();
for(const input of document.querySelectorAll('[data-upload]'))input.onchange=async()=>{const file=input.files[0];if(!file)return;status.textContent='Uploading '+file.name+'...';const base64=await new Promise(r=>{const x=new FileReader;x.onload=()=>r(x.result);x.readAsDataURL(file)});const out=await (await fetch('/admin/upload',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({name:file.name,base64})})).json();if(out.error){status.textContent=out.error;return}document.querySelector('[data-asset="'+input.dataset.upload+'"]').value=out.url;status.textContent='Uploaded. Press Publish changes.'};
form.onsubmit=async e=>{e.preventDefault();config={schemaVersion:1,latestVersion:form.elements.latestVersion.value,minimumVersion:form.elements.minimumVersion.value,forceUpdate:form.elements.forceUpdate.checked,updateTitle:form.elements.updateTitle.value,updateMessage:form.elements.updateMessage.value,updateUrl:form.elements.updateUrl.value,assets:{}};for(const [key] of fields){const url=document.querySelector('[data-asset="'+key+'"]').value.trim();if(url)config.assets[key]=url}const res=await fetch('/admin/config',{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(config)});const out=await res.json();status.textContent=out.error||'Published. New launcher opens will receive this config.'};
</script></body></html>`;
