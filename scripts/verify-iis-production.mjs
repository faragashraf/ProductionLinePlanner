import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { dirname, extname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const artifactRoot = join(repositoryRoot, 'artifacts', 'iis');
const frontendRoot = join(artifactRoot, 'frontend');
const backendRoot = join(artifactRoot, 'backend');
const failures = [];

function assert(condition, message) {
  if (!condition) failures.push(message);
}

function requireFile(path, label) {
  assert(existsSync(path), `Missing ${label}: ${path}`);
  return existsSync(path) ? readFileSync(path, 'utf8') : '';
}

function filesRecursively(root) {
  if (!existsSync(root)) return [];
  return readdirSync(root, { withFileTypes: true }).flatMap(entry => {
    const path = join(root, entry.name);
    return entry.isDirectory() ? filesRecursively(path) : [path];
  });
}

const indexHtml = requireFile(join(frontendRoot, 'index.html'), 'frontend index.html');
const frontendWebConfig = requireFile(join(frontendRoot, 'web.config'), 'frontend IIS web.config');
assert(/<base href="\/app\/">/i.test(indexHtml), 'Frontend index.html must use <base href="/app/">.');
assert(/(?:src|href)="(?:\.\/)?main-[^"]+\.js"/i.test(indexHtml), 'Frontend index.html must reference a hashed main JavaScript bundle.');
assert(/(?:src|href)="(?:\.\/)?polyfills-[^"]+\.js"/i.test(indexHtml), 'Frontend index.html must reference a hashed polyfills JavaScript bundle.');
assert(/(?:src|href)="(?:\.\/)?styles-[^"]+\.css"/i.test(indexHtml), 'Frontend index.html must reference a hashed stylesheet.');
assert(existsSync(join(frontendRoot, 'assets', 'brand', 'manifest.webmanifest')), 'Frontend artifact must contain assets/brand/manifest.webmanifest.');
assert(/<rewrite>/i.test(frontendWebConfig) && /url="index\.html"/i.test(frontendWebConfig), 'Frontend web.config must rewrite client routes to its application-local index.html.');
assert(/\^\/_?\(api\|hubs\)/i.test(frontendWebConfig), 'Frontend web.config must not rewrite API or hub paths.');
assert(!existsSync(join(frontendRoot, 'browser')), 'Deploy the browser contents, not an extra browser directory.');

const textExtensions = new Set(['.css', '.html', '.js', '.json', '.webmanifest', '.config']);
const frontendText = filesRecursively(frontendRoot)
  .filter(path => textExtensions.has(extname(path).toLowerCase()))
  .map(path => readFileSync(path, 'utf8'))
  .join('\n');
for (const forbidden of ['localhost', '127.0.0.1', ':4200', ':5169', '/Dayoub/app', '/Dayoub/api']) {
  assert(!frontendText.toLowerCase().includes(forbidden.toLowerCase()), `Frontend artifact contains forbidden deployment reference: ${forbidden}`);
}
assert(frontendText.includes('http://192.168.1.99:9000'), 'Frontend artifact does not contain the IIS API origin.');
assert(!filesRecursively(frontendRoot).some(path => path.endsWith('.map')), 'Production frontend artifact must not contain source maps.');

requireFile(join(backendRoot, 'ProductionLinePlanner.Api.dll'), 'published API assembly');
requireFile(join(backendRoot, 'ProductionLinePlanner.Api.deps.json'), 'published API dependencies manifest');
requireFile(join(backendRoot, 'ProductionLinePlanner.Api.runtimeconfig.json'), 'published API runtime manifest');
const backendWebConfig = requireFile(join(backendRoot, 'web.config'), 'generated backend IIS web.config');
const productionSettings = requireFile(join(backendRoot, 'appsettings.Production.json'), 'published production configuration');
assert(/AspNetCoreModuleV2/i.test(backendWebConfig), 'Backend web.config must use AspNetCoreModuleV2.');
assert(/processPath="dotnet"/i.test(backendWebConfig), 'Framework-dependent backend web.config must use dotnet as processPath.');
assert(/arguments="\.\\ProductionLinePlanner\.Api\.dll"/i.test(backendWebConfig), 'Backend web.config must target the published API assembly.');
assert(/hostingModel="inprocess"/i.test(backendWebConfig), 'Backend web.config must use the supported in-process hosting model.');
assert(/stdoutLogEnabled="false"/i.test(backendWebConfig), 'Backend web.config must keep stdout logging disabled.');
assert(!existsSync(join(backendRoot, 'appsettings.Development.json')), 'Published backend must not contain appsettings.Development.json.');
assert(!filesRecursively(backendRoot).some(path => ['.cs', '.csproj', '.sln'].includes(extname(path).toLowerCase())), 'Backend artifact must not contain source files.');

try {
  const config = JSON.parse(productionSettings);
  const allowedOrigins = config?.Cors?.AllowedOrigins;
  assert(Array.isArray(allowedOrigins) && allowedOrigins.length === 2 && allowedOrigins.includes('http://dayoub.local') && allowedOrigins.includes('http://192.168.1.99'), 'Production CORS must allow only the approved Dayoub and LAN frontend origins.');
  assert(config?.Cors?.AllowInsecureHttpOrigins === true, 'Production config must explicitly opt in to its local HTTP CORS origin.');
  assert(config?.Cors?.AllowCredentials === true, 'Production CORS must permit credentialed SignalR-compatible requests for the exact origin.');
  assert(config?.Hosting?.EnableHttpsRedirection === false && config?.Hosting?.EnableHsts === false, 'Local HTTP IIS deployment must disable HTTPS redirection and HSTS.');
} catch (error) {
  failures.push(`Could not parse published appsettings.Production.json: ${error.message}`);
}

if (failures.length > 0) {
  console.error('IIS production artifact verification failed:');
  for (const failure of failures) console.error(`- ${failure}`);
  process.exit(1);
}

console.log(`IIS production artifacts verified: ${artifactRoot}`);
