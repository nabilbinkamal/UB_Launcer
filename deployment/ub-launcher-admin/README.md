# UB Launcher admin panel

This small Node service is the control panel for launcher update notices, the home banner, UB logo, and sidebar/social SVG icons. It has no dependency installation step.

It deliberately binds only to `127.0.0.1:8602`. Put it behind the HTTPS reverse proxy as `https://launcher.unitedbangla.top/`; do **not** expose the admin panel directly on the public IP.

Set these environment variables before starting it:

```text
UB_ADMIN_PASSWORD=<a unique 12+ character password>
UB_PUBLIC_BASE_URL=https://launcher.unitedbangla.top
PORT=8602
```

Open `https://launcher.unitedbangla.top/admin`, sign in as `admin`, upload an image or SVG, and press **Publish changes**. The launcher reads the public configuration from `/config.json` on every start. The client accepts HTTPS asset URLs only.

The launcher does not download or run executables from this panel. The update popup only opens the HTTPS release/download link you publish. Set **minimum required version** and enable **force update** to remove the “Later” button for older versions.
