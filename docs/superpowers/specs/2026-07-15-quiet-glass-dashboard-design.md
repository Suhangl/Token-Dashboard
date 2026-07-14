# Quiet Glass Dashboard and Icon Design

## Objective

Refresh Token Dashboard with a quiet, Windows-native interpretation of Liquid Glass while preserving the existing compact layout and monitoring behavior. The result should improve hierarchy and polish without imitating Apple assets or depending on Apple-only APIs.

## Platform Decision

The Prisma Labs `apple-skills` repository is a useful reference for hierarchy, restraint, material layering, and accessibility. Its Liquid Glass implementation guidance targets iOS 26 and SwiftUI, so APIs such as `glassEffect` and `GlassEffectContainer` cannot be used in this WPF/.NET Framework application.

The implementation will therefore be described as **Liquid-Glass-inspired for Windows**. It will build on the application's existing DWM transient backdrop and acrylic support.

## Dashboard Visual Direction

Use the approved **Quiet Glass** direction:

- Preserve the current popup dimensions, provider order, compact density, and overall row structure unless a small change materially improves hierarchy.
- Use a low-chroma charcoal glass surface with restrained background blur.
- Add a subtle translucent gradient, a one-pixel highlight border, and light inner and outer shadows.
- Avoid nested system blur surfaces. Provider sections remain part of one glass surface.
- Keep status color localized to status indicators and meter fills.
- Use subdued green, blue-grey, and neutral slate rather than highly saturated gradients.
- Maintain readable contrast on both light and dark desktop backgrounds.

This is a visual refresh, not a redesign of provider functionality.

## Information Hierarchy

### Codex

The Codex section contains:

- 5-hour allowance meter.
- Weekly allowance meter.
- Codex token usage summary.

Token usage belongs to Codex and must not appear as a global footer metric.

### MiniMax

The MiniMax section contains both:

- 5-hour allowance meter.
- Weekly allowance meter.

Its layout mirrors Codex's two-window structure, excluding Codex-only token usage.

### DeepSeek

The DeepSeek section retains its balance meter and existing balance semantics.

### Global Footer

The footer may contain only global state, such as refresh countdown or provider count. It must not contain Codex-specific token usage.

## Labels and Time Presentation

- `5H`, `W`, and `Balance` are secondary descriptors, not primary headings.
- Render these descriptors with the small, muted typography used in the first Quiet Glass concept: reduced size, reduced contrast, and normal or medium weight.
- The remaining percentage or balance value remains the dominant element.
- Replace `reset 02:18` with the compact t-minus form `−02:18`.
- Do not show the word `reset`.
- Do not display a reset time for weekly allowance rows.
- When a 5-hour reset time is unavailable, omit the t-minus value rather than showing a placeholder that suggests a known reset.
- Preserve the current unavailable-meter behavior: a black meter with no `Unavailable` text.

## Tray Icon Modes

Add a user-selectable tray icon mode:

1. **Default icon**
   - Display the approved open gauge-ring symbol.
   - Use a flat, low-saturation, non-skeuomorphic treatment.
   - Use a single high-contrast foreground that adapts or remains legible on light and dark taskbars.

2. **Codex 5-hour percentage**
   - When a valid Codex 5-hour value is available, show the integer remaining percentage in the tray icon.
   - Use the same circular visual grammar as the default gauge-ring symbol.
   - When the value is unavailable, immediately fall back to the default icon.

The setting persists across restarts. The icon refreshes only when the selected mode, availability, or displayed percentage changes.

At 16 pixels, legibility takes priority over decorative detail. The percentage renderer must handle `0` through `100`; `100` receives a purpose-built compact treatment rather than being silently changed to `99+`.

Generate and cache icon variants carefully, and dispose replaced native icon handles to avoid GDI resource leaks. Validate at common Windows tray sizes and 100%, 125%, 150%, and 200% scaling.

## Application Icon

Create an original application icon derived from the same open gauge-ring symbol:

- Transparent background.
- Vector master asset, preferably SVG.
- Open circular gauge silhouette consistent with the tray icon.
- Slightly more detail than the tray version: restrained glass depth, a soft inner highlight, and a subtle shadow or depth cue.
- Low-to-moderate saturation. A cool slate or blue-grey base may use the same semantic green accent as healthy allowance meters, but the icon must remain calm rather than luminous.
- No copied Codex, OpenAI, Apple, or SF Symbols artwork.
- Export a Windows multi-resolution ICO for the executable and shortcuts, including at least 16, 20, 24, 32, 48, 64, 128, and 256 pixel representations where the build pipeline permits.

The smallest sizes use a simplified optical variant so the ring opening and center remain distinct.

## Interaction and Motion

- Retain the repaired tray hover, dismissal, multi-monitor, DPI, and top/bottom taskbar positioning behavior.
- If animation is added, limit it to subtle opacity or scale transitions around 120–180 ms.
- Respect Windows reduced-motion and reduced-transparency preferences where accessible from the current framework.
- Do not animate the tray icon continuously.

## Compatibility and Fallbacks

- Keep the existing DWM backdrop path where supported.
- Retain a readable translucent charcoal fallback when acrylic/backdrop effects are unavailable.
- Ensure the popup remains readable against light wallpapers, dark wallpapers, and high-detail backgrounds.
- Do not let visual changes alter quota availability rules or fabricate missing Codex reset data.

## Testing and Acceptance

### Automated checks

- Settings serialization and backward-compatible defaults for tray icon mode.
- Icon mode selection and persistence.
- Percentage icon selection for valid values from 0 through 100.
- Default-icon fallback when 5-hour data is unavailable.
- No redundant icon replacement when the displayed state has not changed.
- Existing quota, popup placement, hover, and dismissal tests continue to pass.

### Visual checks

- Popup on light, dark, and detailed desktop backgrounds.
- Top and bottom taskbars.
- Single-monitor and dual-monitor transitions.
- Light and dark taskbar colors.
- 100%, 125%, 150%, and 200% display scaling.
- Default, percentage, unavailable fallback, and exact `100` tray icon states.
- Application icon at Explorer small, medium, large, and shortcut sizes.

## Out of Scope

- Recreating Apple Liquid Glass refraction or morphing behavior.
- Copying Apple, OpenAI, Codex, or SF Symbols visual assets.
- Changing provider APIs, quota-source rules, or refresh scheduling.
- Broad refactoring unrelated to the visual layer and icon lifecycle.
