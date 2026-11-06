#!/usr/bin/env python3
"""Build Windows prerelease packages from the single committed checkout and verified local caches. No CI or network."""
import argparse, hashlib, json, os, re, shutil, subprocess, tempfile, zipfile
from pathlib import Path

def digest(path, algorithm='sha256'):
    return hashlib.new(algorithm, path.read_bytes()).hexdigest()

def checked(args, cwd):
    return subprocess.check_output(args, cwd=cwd, text=True).strip()

def package(repo, version, cache, output):
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?', version):
        raise ValueError('Invalid version')
    if checked(['git','status','--porcelain','--untracked-files=no'], repo):
        raise ValueError('Commit tracked changes before packaging')
    untracked=checked(['git','ls-files','--others','--exclude-standard'],repo).splitlines()
    if any(p.startswith('src/') for p in untracked): raise ValueError('Untracked source is not represented by the commit')
    commit=checked(['git','rev-parse','HEAD'],repo); tree=checked(['git','rev-parse','HEAD^{tree}'],repo)
    sdk=checked(['dotnet','--version'],repo)
    output=output.resolve(); cache=cache.resolve()
    if output==Path(output.anchor) or output==repo or repo in output.parents: raise ValueError('Choose an artifact directory outside the checkout')
    output.mkdir(parents=True,exist_ok=True)
    runtimes=json.loads((repo/'third_party/dotnet-windows-runtime.json').read_text())
    for rid,info in runtimes.items():
        runtime=cache/('official-dotnet-runtime-'+info['version']+'-'+rid+'.zip')
        if digest(runtime,'sha512')!=info['sha512']: raise ValueError('Unreviewed runtime archive: '+rid)
        for name,expected in info['documents'].items():
            if digest(repo/'third_party/licenses'/name)!=expected:raise ValueError('Runtime notice changed: '+name)
    with tempfile.TemporaryDirectory(prefix='openwiimote-windows-',dir=output) as temporary:
        stage=Path(temporary)
        for rid,info in runtimes.items():
            name='openwiimote-'+version+'-'+rid; archive=output/(name+'.zip')
            if archive.exists() or archive.with_suffix('.zip.sha256').exists():raise ValueError('Artifact already exists: '+name)
            payload=stage/name
            feed=stage/'empty-feed'; feed.mkdir(exist_ok=True)
            subprocess.run(['dotnet','restore','src/OpenWiimote.App/OpenWiimote.App.csproj','-r',rid,
                '--source',str(feed),'-p:NuGetAudit=false'],cwd=repo,check=True)
            # --no-restore requires the matching RID apphost already cached. The apphost
            # searches only the bundled runtime, so no global .NET installation is needed.
            subprocess.run(['dotnet','publish','src/OpenWiimote.App/OpenWiimote.App.csproj','-c','Release','-r',rid,
                '--no-restore','--self-contained','false','--disable-build-servers','-m:1','-p:UseAppHost=true',
                '-p:AppHostDotNetSearch=AppRelative','-p:AppHostRelativeDotNet=runtime',
                '-p:Version='+version,'-p:SourceRevisionId='+commit,'-p:DebugType=None','-p:DebugSymbols=false',
                '-o',str(payload)],cwd=repo,check=True)
            subprocess.run(['python3','scripts/bundle-notices.py','--deps',str(payload/'OpenWiimote.deps.json'),
                '--output',str(payload),'--sdk-version',sdk,'--rid',rid],cwd=repo,check=True)
            runtime=cache/('official-dotnet-runtime-'+info['version']+'-'+rid+'.zip')
            with zipfile.ZipFile(runtime) as z:
                for entry in z.infolist():
                    path=Path(entry.filename.replace('\\','/'))
                    if path.is_absolute() or '..' in path.parts or (entry.external_attr>>16)&0o170000==0o120000:
                        raise ValueError('Unsafe runtime entry')
                z.extractall(payload/'runtime')
            shutil.copy2(repo/'third_party/dotnet-windows-runtime.json',payload/'third_party/dotnet-windows-runtime.json')
            for notice in info['documents']:shutil.copy2(repo/'third_party/licenses'/notice,payload/'third_party/licenses'/notice)
            with (payload/'THIRD-PARTY-NOTICES.md').open('a') as notices:
                notices.write('\n## Bundled Windows .NET runtime\n\nMicrosoft .NET '+info['version']+' ('+rid+'), verified against the pinned official SHA-512.\n')
                for notice in info['documents']:notices.write('\n- ['+notice+'](third_party/licenses/'+notice+')\n')
            (payload/'docs').mkdir(exist_ok=True)
            for doc in ['Windows-Setup.md','Pointer-Setup.md','Licensing.md']:
                shutil.copy2(repo/'docs'/doc,payload/'docs'/doc)
            (payload/'README.md').write_text('# OpenWiimote Windows prerelease\n\nDouble-click **OpenWiimote.exe**. The .NET runtime is included; no SDK or administrator rights are needed for pointer mode.\n\nRead [Windows setup](docs/Windows-Setup.md) and [sensor-bar calibration](docs/Pointer-Setup.md).\n\n'+
                ('This native ARM64 build supports pointer mode. For virtual gamepads on Windows 11 ARM, use the x64 package and the official ARM64 ViGEmBus driver.\n\n' if rid=='win-arm64' else 'This x64 build is intended for Intel/AMD Windows and Windows 11 ARM x64 emulation. Gamepad output requires the separately installed, signed ViGEmBus driver.\n\n')+
                'This is a prerelease candidate; physical pairing, pointer feel and two-remote gamepad acceptance remain required. MotionPlus is unavailable. Keep credentials and configuration outside this folder.\n',encoding='utf-8')
            metadata={'version':version,'sourceCommit':commit,'sourceTree':tree,'platform':'windows',
                'architecture':rid.removeprefix('win-'),'packageType':'framework-dependent-with-private-runtime',
                'bundledRuntime':info,'sourceLicense':'ISC','githubCI':'off','hardwareAcceptance':'pending',
                'motionPlus':{'status':'experimental','available':False}}
            (payload/'release.json').write_text(json.dumps(metadata,indent=2)+'\n')
            for p in payload.rglob('*.pdb'):p.unlink()
            for p in payload.rglob('*'):
                if p.is_symlink() or p.name in {'.git','AGENTS.md','settings.json','service.env'} or p.name.startswith('.env'):
                    raise ValueError('Unintended package content: '+str(p.relative_to(payload)))
            hashes={p.relative_to(payload).as_posix():digest(p) for p in sorted(payload.rglob('*')) if p.is_file()}
            (payload/'SHA256SUMS.json').write_text(json.dumps(hashes,indent=2)+'\n')
            artifact=stage/(name+'.zip')
            with zipfile.ZipFile(artifact,'w',zipfile.ZIP_DEFLATED) as z:
                for p in sorted(payload.rglob('*')):
                    if p.is_file():z.write(p,name+'/'+p.relative_to(payload).as_posix())
            os.link(artifact,archive)
            with archive.with_suffix('.zip.sha256').open('x') as file:file.write(digest(archive)+'  '+archive.name+'\n')
            print('WINDOWS_PACKAGE_READY '+str(archive),flush=True)

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('version');parser.add_argument('--runtime-cache',type=Path,required=True);parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();repo=Path(__file__).resolve().parents[1]
    package(repo,args.version,args.runtime_cache,args.output)
if __name__=='__main__':main()
