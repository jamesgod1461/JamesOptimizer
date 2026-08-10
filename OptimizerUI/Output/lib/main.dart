import 'package:flutter/material.dart';
import 'package:window_manager/window_manager.dart';
import 'ui/dashboard.dart';
import 'ui/theme.dart';

void main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await windowManager.ensureInitialized();

  WindowOptions windowOptions = const WindowOptions(
    size: Size(1350, 850),
    center: true,
    backgroundColor: Colors.transparent,
    skipTaskbar: false,
    titleBarStyle: TitleBarStyle.hidden,
  );
  
  await windowManager.waitUntilReadyToShow(windowOptions, () async {
    await windowManager.show();
    await windowManager.focus();
  });

  runApp(const JamesOptimizerApp());
}

class JamesOptimizerApp extends StatelessWidget {
  const JamesOptimizerApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: '⚡ Optimizer by James God',
      debugShowCheckedModeBanner: false,
      theme: AppTheme.darkTheme,
      home: const Dashboard(),
    );
  }
}
