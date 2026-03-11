# Analyze expected JSON structure from smartctl -j -a output
# This shows what structure we expect for self-test status

Write-Output "=== Expected JSON structure for ATA drives ==="
Write-Output @'
{
  "ata_smart_data": {
    "self_test": {
      "status": {
        "value": 240,        // 240-255 = test in progress with % remaining (lower 7 bits)
        "string": "in progress"  // or "completed without error", "aborted", etc.
      },
      "polling_minutes": {
        "short": 2,
        "extended": 120
      }
    }
  },
  "ata_smart_self_test_log": {
    "standard": {
      "table": [
        {
          "type": { "string": "Short offline" },
          "status": { "string": "Completed without error", "value": 0 },
          "lifetime_hours": 12345
        }
      ]
    }
  }
}
'@

Write-Output ""
Write-Output "=== Expected JSON structure for NVMe drives ==="
Write-Output @'
{
  "device": { "type": "nvme" },
  "nvme_self_test_log": {
    "current_self_test": {
      "self_test_status": { "value": 1 },  // 1 = in progress
      "self_test_code": { },
      "self_test_completion": 50  // % complete
    }
  }
}
'@

Write-Output ""
Write-Output "=== Key difference: status.value format ==="
Write-Output "ATA: status.value 240-255 = in progress (lower 7 bits = % remaining)"
Write-Output "NVMe: current_self_test.self_test_status.value 1 = in progress"
Write-Output ""
Write-Output "=== Problem analysis ==="
Write-Output "If smartctl returns:"
Write-Output "  - status.value = 243  (means: in progress, 43% remaining)"
Write-Output "  - status.string = 'in progress'"
Write-Output ""
Write-Output "Our parser checks:"
Write-Output "  1. status.string.ToLowerInvariant().Contains('in progress') ✓"
Write-Output "  2. status.value > 0 && status.value <= 255 ✓"
Write-Output ""
Write-Output "BUT if the JSON uses different keys or nesting, parser fails!"
Write-Output ""
Write-Output "=== Common smartctl JSON variants ==="
Write-Output "Variant A (most common): ata_smart_data.self_test.status"
Write-Output "Variant B: ata_smart_self_test_log.standard.table[0].status (first entry is in-progress)"
Write-Output "Variant C: Direct ata_smart_self_test_log.table[0].status"