import 'package:flutter/material.dart';
import '../../core/optimization_engine.dart';
import '../theme.dart';

class TweaksPage extends StatefulWidget {
  const TweaksPage({super.key});

  @override
  State<TweaksPage> createState() => _TweaksPageState();
}

class _TweaksPageState extends State<TweaksPage> {
  String _statusMessage = 'Ready';
  double _progress = 0;

  @override
  void initState() {
    super.initState();
    OptimizationEngine.onProgress = (percent, message) {
      if (mounted) {
        setState(() {
          _progress = percent / 100;
          _statusMessage = message;
        });
      }
    };
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Text(
          'Advanced Tweaks',
          style: TextStyle(fontSize: 24, fontWeight: FontWeight.bold, color: Colors.white),
        ),
        const SizedBox(height: 10),
        Text(_statusMessage, style: const TextStyle(color: AppTheme.primaryBlue)),
        const SizedBox(height: 5),
        LinearProgressIndicator(value: _progress, backgroundColor: AppTheme.surfaceGlass, color: AppTheme.primaryBlue),
        const SizedBox(height: 20),
        Expanded(
          child: GridView.count(
            crossAxisCount: 3,
            crossAxisSpacing: 20,
            mainAxisSpacing: 20,
            children: [
              _buildTweakCard('Gaming Boost', 'Unlocks Ultimate Performance Plan', () => OptimizationEngine.runGamingBoostAsync()),
              _buildTweakCard('Elite Tweaks', 'SSD & Network Optimization', () => OptimizationEngine.runEliteTweaksAsync()),
              _buildTweakCard('Network Flush', 'Reset TCP/IP & Winsock', () => OptimizationEngine.runNetworkFlushAsync()),
              _buildTweakCard('Fast Clean', 'Clear Temp & Prefetch', () => OptimizationEngine.runFastCleanAsync()),
            ],
          ),
        ),
      ],
    );
  }

  Widget _buildTweakCard(String title, String subtitle, VoidCallback onStart) {
    return Container(
      padding: const EdgeInsets.all(20),
      decoration: BoxDecoration(
        color: AppTheme.surfaceGlass,
        border: Border.all(color: AppTheme.borderGlass),
        borderRadius: BorderRadius.circular(16),
      ),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Text(title, style: const TextStyle(color: Colors.white, fontSize: 18, fontWeight: FontWeight.bold)),
          const SizedBox(height: 10),
          Text(subtitle, textAlign: TextAlign.center, style: const TextStyle(color: AppTheme.textMuted, fontSize: 12)),
          const Spacer(),
          ElevatedButton(
            style: ElevatedButton.styleFrom(
              backgroundColor: AppTheme.primaryBlue.withOpacity(0.2),
              side: const BorderSide(color: AppTheme.primaryBlue),
              shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(30)),
            ),
            onPressed: onStart,
            child: const Text('APPLY', style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold)),
          ),
        ],
      ),
    );
  }
}
