import 'package:flutter/material.dart';
import 'package:window_manager/window_manager.dart';
import 'theme.dart';
import 'pages/home_page.dart';
import 'pages/tweaks_page.dart';
import 'pages/about_page.dart';

class Dashboard extends StatefulWidget {
  const Dashboard({super.key});

  @override
  State<Dashboard> createState() => _DashboardState();
}

class _DashboardState extends State<Dashboard> {
  int _selectedIndex = 0;

  final List<Widget> _pages = [
    const HomePage(),
    const TweaksPage(),
    const AboutPage(),
  ];

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.transparent,
      body: Container(
        margin: const EdgeInsets.all(10),
        decoration: BoxDecoration(
          color: AppTheme.background,
          borderRadius: BorderRadius.circular(20),
          boxShadow: [
            BoxShadow(
              color: AppTheme.primaryBlue.withOpacity(0.5),
              blurRadius: 20,
            ),
          ],
        ),
        child: Column(
          children: [
            _buildTitleBar(),
            Expanded(
              child: Row(
                children: [
                  _buildSidebar(),
                  Expanded(
                    child: Padding(
                      padding: const EdgeInsets.all(20.0),
                      child: _pages[_selectedIndex],
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildTitleBar() {
    return GestureDetector(
      onPanUpdate: (details) {
        windowManager.startDragging();
      },
      child: Container(
        height: 40,
        decoration: const BoxDecoration(
          color: Color(0x0DFFFFFF),
          borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
        ),
        child: Row(
          children: [
            const SizedBox(width: 20),
            const Text('⚡ ', style: TextStyle(color: AppTheme.primaryBlue, fontSize: 16)),
            const Text('JAMES', style: TextStyle(color: AppTheme.primaryBlue, fontSize: 16, fontWeight: FontWeight.bold)),
            const Text(' OPTIMIZER', style: TextStyle(color: Colors.white, fontSize: 16)),
            const Spacer(),
            IconButton(
              icon: const Icon(Icons.info_outline, color: AppTheme.textMuted, size: 20),
              onPressed: () => setState(() => _selectedIndex = 2),
            ),
            IconButton(
              icon: const Icon(Icons.minimize, color: AppTheme.textMuted, size: 20),
              onPressed: () => windowManager.minimize(),
            ),
            IconButton(
              icon: const Icon(Icons.close, color: AppTheme.textMuted, size: 20),
              onPressed: () => windowManager.close(),
            ),
            const SizedBox(width: 10),
          ],
        ),
      ),
    );
  }

  Widget _buildSidebar() {
    return Container(
      width: 230,
      decoration: const BoxDecoration(
        color: Color(0x0AFFFFFF),
        border: Border(right: BorderSide(color: AppTheme.borderGlass)),
      ),
      padding: const EdgeInsets.all(15),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text('DASHBOARD', style: TextStyle(color: Color(0x66FFFFFF), fontSize: 10, fontWeight: FontWeight.bold)),
          const SizedBox(height: 10),
          _sidebarItem(0, '🖥️ System Monitoring'),
          const SizedBox(height: 20),
          const Text('TOOLS', style: TextStyle(color: Color(0x66FFFFFF), fontSize: 10, fontWeight: FontWeight.bold)),
          const SizedBox(height: 10),
          _sidebarItem(1, '⚙️ Advanced Tweaks'),
        ],
      ),
    );
  }

  Widget _sidebarItem(int index, String title) {
    bool isSelected = _selectedIndex == index;
    return GestureDetector(
      onTap: () => setState(() => _selectedIndex = index),
      child: Container(
        height: 45,
        margin: const EdgeInsets.only(bottom: 5),
        decoration: BoxDecoration(
          color: isSelected ? const Color(0x3300D2FF) : Colors.transparent,
          borderRadius: BorderRadius.circular(8),
        ),
        padding: const EdgeInsets.symmetric(horizontal: 15),
        alignment: Alignment.centerLeft,
        child: Text(
          title,
          style: TextStyle(
            color: isSelected ? AppTheme.primaryBlue : AppTheme.textMuted,
            fontWeight: isSelected ? FontWeight.bold : FontWeight.normal,
            fontSize: 14,
          ),
        ),
      ),
    );
  }
}
