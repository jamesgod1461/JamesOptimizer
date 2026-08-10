import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';
import '../theme.dart';

class AboutPage extends StatelessWidget {
  const AboutPage({super.key});

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          const Text('⚡ JAMES OPTIMIZER', style: TextStyle(fontSize: 28, fontWeight: FontWeight.w900, color: AppTheme.primaryBlue)),
          const SizedBox(height: 30),
          Container(
            width: 400,
            padding: const EdgeInsets.all(30),
            decoration: BoxDecoration(
              color: AppTheme.surfaceGlass,
              border: Border.all(color: AppTheme.borderGlass),
              borderRadius: BorderRadius.circular(12),
            ),
            child: Column(
              children: [
                const Text('APPLICATION INFO', style: TextStyle(color: AppTheme.textMuted, fontWeight: FontWeight.bold)),
                const SizedBox(height: 10),
                const Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [Text('Version:', style: TextStyle(color: AppTheme.textMuted)), Text('1.0.0 (Flutter Rewrite)', style: TextStyle(color: Colors.white))],
                ),
                const SizedBox(height: 30),
                const Text('DEVELOPER INFO', style: TextStyle(color: AppTheme.textMuted, fontWeight: FontWeight.bold)),
                const SizedBox(height: 10),
                const Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [Text('Active Developer:', style: TextStyle(color: AppTheme.textMuted)), Text('James God', style: TextStyle(color: Colors.white))],
                ),
                const SizedBox(height: 20),
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceEvenly,
                  children: [
                    IconButton(icon: const Icon(Icons.video_library, color: Colors.red), onPressed: () => launchUrl(Uri.parse('https://www.youtube.com/@jamesgod7'))),
                    IconButton(icon: const Icon(Icons.camera_alt, color: Colors.pink), onPressed: () => launchUrl(Uri.parse('https://www.instagram.com/jamesgod3127'))),
                    IconButton(icon: const Icon(Icons.language, color: Colors.white), onPressed: () => launchUrl(Uri.parse('https://guns.lol/jamesgod'))),
                  ],
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
