from pathlib import Path
p = Path('DiskChecker.Infrastructure/Persistence/DiskCardRepository.cs')
text = p.read_text()
idx = text.index('public async Task<TestSession?> GetTestSessionWithoutSamplesAsync(int sessionId)')
until = text.index('FirstOrDefaultAsync();', idx)
if 'SeekResultsJson' not in text[idx:until]:
    target = 'SmartAfterJson = t.SmartAfterJson'
    pos = text.index(target, idx) + len(target)
    insert = ',\n                SeekResultsJson = t.SeekResultsJson,\n                Sanitize1ResultJson = t.Sanitize1ResultJson,\n                Sanitize2ResultJson = t.Sanitize2ResultJson,\n                AnomaliesJson = t.AnomaliesJson'
    text = text[:pos] + insert + text[pos:]
    p.write_text(text)
