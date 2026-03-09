namespace DiskChecker.Core.Models {
      public enum SmartaSelfTestStatus {
          Unknown = 0,
          CompletedWithoutError = 16,
          AbortedByUser = 1,
          AbortedByHost = 2,
          FatalError = 3,
          ErrorUnknown = 4,
          ErrorElectrical = 5,
          ErrorServo = 6,
          ErrorRead = 7,
          ErrorHandling = 8,
          InProgress = 15
      }
    }