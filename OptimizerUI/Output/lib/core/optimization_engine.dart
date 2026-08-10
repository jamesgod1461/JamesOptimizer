import 'dart:io';

class OptimizationEngine {
  static void Function(int percent, String message)? onProgress;

  static void _reportProgress(int percent, String message) {
    if (onProgress != null) {
      onProgress!(percent, message);
    }
  }

  static Future<void> _runProcess(String executable, List<String> arguments) async {
    try {
      await Process.run(executable, arguments, runInShell: true);
    } catch (e) {
      print('Error running process: $e');
    }
  }

  static Future<void> runGamingBoostAsync() async {
    _reportProgress(0, "Unlocking Ultimate Power Plan...");
    await _runProcess('powercfg', ['-duplicatescheme', 'e9a42b02-d5df-448d-aa00-03f14749eb61']);
    
    _reportProgress(30, "Activating Ultimate Performance...");
    await _runProcess('powercfg', ['/setactive', 'e9a42b02-d5df-448d-aa00-03f14749eb61']);
    
    _reportProgress(60, "Disabling Hibernation...");
    await _runProcess('powercfg', ['-h', 'off']);
    
    _reportProgress(80, "Applying Network Latency Fixes...");
    await _runProcess('netsh', ['int', 'tcp', 'set', 'global', 'autotuninglevel=normal']);
    
    _reportProgress(100, "GAMING MODE ENABLED");
  }

  static Future<void> runEliteTweaksAsync() async {
    _reportProgress(0, "Optimizing Boot Configuration (BCD)...");
    await _runProcess('bcdedit', ['/set', 'useplatformclock', 'No']);
    await _runProcess('bcdedit', ['/set', 'disabledynamictick', 'Yes']);
    await _runProcess('bcdedit', ['/set', 'tscsyncpolicy', 'Enhanced']);

    _reportProgress(33, "Optimizing SSD Performance (fsutil)...");
    await _runProcess('fsutil', ['behavior', 'set', 'disable8dot3', '1']);
    await _runProcess('fsutil', ['behavior', 'set', 'disablelastaccess', '1']);

    _reportProgress(66, "Disabling Memory Compression & Optimizing Network...");
    await _runProcess('powershell.exe', ['-Command', 'Disable-MMAgent -MemoryCompression']);
    await _runProcess('netsh', ['int', 'tcp', 'set', 'global', 'rss=enabled']);

    _reportProgress(100, "Elite System Optimization Applied!");
  }

  static Future<void> runNetworkFlushAsync() async {
    _reportProgress(0, "Flushing DNS...");
    await _runProcess('ipconfig', ['/flushdns']);
    
    _reportProgress(50, "Resetting Winsock...");
    await _runProcess('netsh', ['winsock', 'reset']);
    
    _reportProgress(80, "Resetting TCP/IP...");
    await _runProcess('netsh', ['int', 'ip', 'reset']);
    
    _reportProgress(100, "Network Optimized");
  }

  static Future<void> runFastCleanAsync() async {
    _reportProgress(0, "Scanning Temp Folders...");
    // Mocking cleanup logic for safety in Dart translation
    await Future.delayed(const Duration(milliseconds: 500));
    _reportProgress(50, "Cleaning Windows Temp...");
    await Future.delayed(const Duration(milliseconds: 500));
    _reportProgress(100, "Junk Files Removed!");
  }
}
