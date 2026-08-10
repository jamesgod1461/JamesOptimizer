import 'package:flutter/material.dart';

class AppTheme {
  static const Color primaryBlue = Color(0xFF00D2FF);
  static const Color primaryPurple = Color(0xFF3A7BD5);
  static const Color accentRed = Color(0xFFFF6B6B);
  static const Color accentGreen = Color(0xFF00FF88);
  static const Color background = Color(0xFF0A0A0E);
  static const Color surfaceGlass = Color(0x1AFFFFFF);
  static const Color borderGlass = Color(0x33FFFFFF);
  static const Color textMuted = Color(0x88FFFFFF);

  static ThemeData get darkTheme {
    return ThemeData(
      brightness: Brightness.dark,
      scaffoldBackgroundColor: background,
      primaryColor: primaryBlue,
      fontFamily: 'Segoe UI',
      colorScheme: const ColorScheme.dark(
        primary: primaryBlue,
        secondary: accentRed,
      ),
    );
  }
}
