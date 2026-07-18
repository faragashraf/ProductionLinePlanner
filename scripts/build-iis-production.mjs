import { cpSync, existsSync, mkdirSync, rmSync, copyFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';

const action = process.argv[2] ?? 'all';
const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, '..');
const frontendRoot = join(repositoryRoot, 'src', 'frontend');
const apiProject = join(repositoryRoot, 'src', 'backend', 'ProductionLinePlanner.Api', 'ProductionLinePlanner.Api.csproj');
const artifactRoot = join(repositoryRoot, 'artifacts', 'iis');
const frontendArtifact = join(artifactRoot, 'frontend');
const frontendBuildArtifact = join(artifactRoot, '.frontend-build');
const backendArtifact = join(artifactRoot, 'backend');
const frontendWebConfig = join(scriptDirectory, 'iis', 'frontend.web.config');

if (!['frontend', 'backend', 'all'].includes(action)) {
  throw new Error('Usage: node scripts/build-iis-production.mjs <frontend|backend|all>');
}

function run(command, args, cwd) {
  console.log(`> ${command} ${args.join(' ')}`);
  execFileSync(command, args, { cwd, stdio: 'inherit' });
}

function requirePath(path, description) {
  if (!existsSync(path)) throw new Error(`Missing ${description}: ${path}`);
}

function buildFrontend() {
  rmSync(frontendArtifact, { recursive: true, force: true });
  rmSync(frontendBuildArtifact, { recursive: true, force: true });
  mkdirSync(frontendBuildArtifact, { recursive: true });

  const angularCli = join(frontendRoot, 'node_modules', '.bin', process.platform === 'win32' ? 'ng.cmd' : 'ng');
  requirePath(angularCli, 'Angular CLI; run npm ci in src/frontend first');
  run(angularCli, [
    'build', '--configuration', 'production', '--base-href', '/', '--output-path', frontendBuildArtifact
  ], frontendRoot);

  const browserOutput = join(frontendBuildArtifact, 'browser');
  requirePath(browserOutput, 'Angular browser output');
  cpSync(browserOutput, frontendArtifact, { recursive: true });
  copyFileSync(frontendWebConfig, join(frontendArtifact, 'web.config'));
  rmSync(frontendBuildArtifact, { recursive: true, force: true });
  requirePath(join(frontendArtifact, 'index.html'), 'frontend index.html');
  console.log(`Frontend artifact: ${frontendArtifact}`);
}

function publishBackend() {
  rmSync(backendArtifact, { recursive: true, force: true });
  mkdirSync(artifactRoot, { recursive: true });
  run('dotnet', [
    'publish', apiProject, '--configuration', 'Release', '--output', backendArtifact, '--no-self-contained'
  ], repositoryRoot);

  rmSync(join(backendArtifact, 'appsettings.Development.json'), { force: true });
  requirePath(join(backendArtifact, 'ProductionLinePlanner.Api.dll'), 'published API assembly');
  requirePath(join(backendArtifact, 'web.config'), 'generated ASP.NET Core IIS web.config');
  console.log(`Backend artifact: ${backendArtifact}`);
}

if (action === 'all') rmSync(artifactRoot, { recursive: true, force: true });
if (action === 'frontend' || action === 'all') buildFrontend();
if (action === 'backend' || action === 'all') publishBackend();
