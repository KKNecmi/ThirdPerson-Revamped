# CS2 - ThirdPerson Revamped

[![GitHub release](https://img.shields.io/github/release/KKNecmi/ThirdPerson-Revamped?include_prereleases=&sort=semver&color=blue)](https://github.com/KKNecmi/ThirdPerson-Revamped/releases/)
[![License](https://img.shields.io/badge/License-GPLv3-blue)](#license)
[![issues - ThirdPerson-Revamped](https://img.shields.io/github/issues/KKNecmi/ThirdPerson-Revamped?color=darkgreen)](https://github.com/KKNecmi/ThirdPerson-Revamped/issues)

A modern, improved third-person camera plugin for Counter-Strike 2.  
🧠 Built for performance, smooth transitions, and flexibility.

---

## 🧩 Dependency Included

This plugin uses a **modified version** of `CS2TraceRay`.  
Original: [https://github.com/schwarper/CS2TraceRay](https://github.com/schwarper/CS2TraceRay)  
Modified version is included in this repository for compatibility.

Please refer to the license terms of the original project.


---

## ✅ Features
- Toggleable third-person view (`!tp`, `!thirdperson`, or `css_thirdperson`)
- Smooth camera transitions (optional)
- Admin-only access (configurable)
- Configurable messages and behavior

<p align="left"> <img src="https://github.com/KKNecmi/ThirdPerson-Revamped/blob/main/ThirdPerson/images/ScreenShotThirdPerson.png" alt="ThirdPerson Screenshot" width="300"/> </p>

---

## 📥 Installation
> Requires [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)

1. Download or clone this repository.
2. Place the plugin `.dll` in your `counterstrikesharp/plugins/` directory.
3. Open your `counterstrikesharp/gamedata/gamedata.json` file.
4. Add the following section from the included `CS2TraceRay.gamedata.json` file:
   [CS2TRACERAY](https://raw.githubusercontent.com/KKNecmi/ThirdPerson-Revamped/refs/heads/main/CS2TraceRay/CS2TraceRay.gamedata.json)
5. (Optional) Customize the config file at `configs/plugins/ThirdPersonRevamped/ThirdPersonRevamped.json`.

---

## ⚙️ Configuration

📁 **Default Config File (`ThirdPersonRevamped.json`):**
```json
{
  "OnActivated": "| ThirdPerson Activated",
  "OnDeactivated": "| ThirdPerson Deactivated",
  "Prefix": " [{BLUE}ThirdPerson Revamped",
  "UseOnlyAdmin": false,
  "OnlyAdminFlag": "@css/slay",
  "NoPermission": "You dont have access to this command.",
  "UseSmoothCam": true,
  "SmoothCamDuration": 0.05,
  "StripOnUse": false,
  "ConfigVersion": 1
}
