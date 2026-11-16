"""Package only this revision after checking validation matches all CAD files."""
from pathlib import Path
import hashlib
import json
import zipfile

ROOT=Path(__file__).resolve().parent


def main():
    parameters=json.loads((ROOT/'parameters.json').read_text(encoding='utf-8'))
    report=json.loads((ROOT/'output/verification.json').read_text(encoding='utf-8'))
    assert report['passed'] and report['revision']==parameters['revision']
    for name,digest in report['files_sha256'].items():
        assert hashlib.sha256((ROOT/name).read_bytes()).hexdigest()==digest,('Stale validation',name)
    files=[ROOT/'README.md',ROOT/'parameters.json',*sorted(ROOT.glob('*.py')),
           *sorted((ROOT/'output').glob('*.step')),*sorted((ROOT/'output').glob('*.stl')),
           *sorted((ROOT/'output').glob('*.json')),*sorted((ROOT/'previews').glob('*.png'))]
    target=ROOT/('ELTS_VIVE_Tracker_3_Rev_'+parameters['revision']+'.zip')
    with zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED) as archive:
        for path in files: archive.write(path,path.relative_to(ROOT))
    with zipfile.ZipFile(target) as archive: assert archive.testzip() is None
    print(f'{target}\n{len(files)} files; {target.stat().st_size:,} bytes')


if __name__=='__main__': main()
