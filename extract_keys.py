
import json
import sys
sys.stdout.reconfigure(encoding='utf-8')

def extract_keys(filepath, label):
    with open(filepath, 'r', encoding='utf-8') as f:
        data = json.load(f)
    
    surface_test_keys = {}
    profile_related_keys = {}
    
    for k, v in data.items():
        if k.startswith('SurfaceTest.'):
            surface_test_keys[k] = v
        if 'Profile' in k or 'profile' in k:
            profile_related_keys[k] = v
    
    return surface_test_keys, profile_related_keys

base = r'D:\DiskChecker\DiskChecker.UI.Avalonia\Locales'

for lang in ['cs', 'en']:
    path = f'{base}\\{lang}.json'
    st_keys, prof_keys = extract_keys(path, lang)
    
    # Write to separate output files to avoid encoding issues
    with open(f'D:/DiskChecker/{lang}_surfacetest.txt', 'w', encoding='utf-8') as f:
        f.write(f'=== {lang.upper()}.json - SurfaceTest.* keys ({len(st_keys)}) ===\n')
        for k in sorted(st_keys):
            f.write(f'KEY: {k}\nCS: {st_keys[k]}\n\n')
    
    with open(f'D:/DiskChecker/{lang}_profile.txt', 'w', encoding='utf-8') as f:
        f.write(f'=== {lang.upper()}.json - Profile-related keys ({len(prof_keys)}) ===\n')
        for k in sorted(prof_keys):
            f.write(f'KEY: {k}\nVAL: {prof_keys[k]}\n\n')

print("Done! Output files written.")
