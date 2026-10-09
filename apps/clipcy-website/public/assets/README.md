# Asset Inventory

## Logo and Icon

**Source:** `apps/clipcy-windows/Assets/Square150x150Logo.png`  
**Dimensions:** 150×150px  
**Format:** PNG  
**Description:** Blue gradient rounded square with white stacked paper layers  
**Website derivatives:**
- `logo-192.png` - 192×192px for manifest
- `logo-512.png` - 512×512px for manifest
- `favicon.ico` - 16×16, 32×32, 48×48 sizes
- `apple-touch-icon.png` - 180×180px

## Product Screenshots

**Status:** App build exists but screenshot capture blocked - requires running the native app with controlled synthetic clipboard data.

**Planned captures:**
1. **Hero screenshot** - Main popup with pinned items, history, search field, and adjacent preview panel
2. **Feature detail: History/Search** - Close-up of search interaction
3. **Feature detail: Pinning** - Pinned section with Ctrl+P hint visible
4. **Feature detail: Preview** - Preview panel showing text/image content
5. **Workflow: Keycaps** - Alt+V, arrows, Enter, Esc, Ctrl+P visual reference

**Specifications:**
- Capture real Windows 11 app UI (not mock-ups)
- Light theme (can show one dark theme example)
- Synthetic safe content only (no personal clipboard data)
- Display shortcuts/keycaps where relevant
- AVIF + WebP with PNG fallback
- Explicit dimensions for layout shift prevention
- Alt text describes workflow/UI state shown

**Optimization targets:**
- Hero: ≤150KB AVIF
- Details: ≤80KB each

**Provenance:** All screenshots captured from actual Clipcy build at version 0.4.2, Windows 11, light theme.

## Current Status

**Completed:**
- Logo source copied to `public/assets/logo-source.png`
- Design tokens and responsive specifications documented

**Blocked:**
- Screenshot capture requires:
  1. Running `apps/clipcy-windows/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/Clipcy.exe`
  2. Populating safe synthetic clipboard examples
  3. Capturing actual UI states
  4. Optimizing to target formats/sizes

**Workaround:** Can complete textual content, layout structure, and placeholder dimensions. Screenshots must be captured before final verification and publication.

## Favicon Generation

Once screenshots complete, generate website icon derivatives:

```bash
# 192px and 512px for manifest
magick public/assets/logo-source.png -resize 192x192 public/assets/logo-192.png
magick public/assets/logo-source.png -resize 512x512 public/assets/logo-512.png

# Apple touch icon
magick public/assets/logo-source.png -resize 180x180 public/assets/apple-touch-icon.png

# Multi-size favicon.ico
magick public/assets/logo-source.png -define icon:auto-resize=16,32,48 public/assets/favicon.ico
```

## No Third-Party Assets

All visual assets are original or derived from the app's own resources. No stock photos, icon libraries, or external brand logos.
