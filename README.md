# CS2 - ThirdPerson Revamped

A modern, improved third-person camera plugin for Counter-Strike 2.  
🧠 Built for performance, smooth transitions, and flexibility.

---

## ✅ Features
- Toggleable third-person view (`!tp`, `!thirdperson`, or `css_thirdperson`)
- Smooth camera transitions (optional)
- Admin-only access (configurable)
- Configurable messages and behavior

---

## 📥 Installation
> Requires [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)

1. Download or clone this repository.
2. Place the plugin `.dll` in your `cs2/plugins/` directory.
3. (Optional) Customize the config file at `configs/plugins/ThirdPersonRevamped/ThirdPersonRevamped.json`.

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
