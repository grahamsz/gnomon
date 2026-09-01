# Browser companion (Chrome and Edge)

The extension sends the active tab's **hostname only** to
`http://127.0.0.1:45981`. It does not collect or send URLs, paths, query strings,
titles, page content, or history.

The Windows MSI includes these files in its `Browser Companion` folder. From the
Gnomon tray menu, choose **Set up Chrome companion** for a guided setup. Chrome
requires you to enable Developer mode and choose **Load unpacked** for extensions
that are not yet in the Chrome Web Store. Pin the extension to see the current
hostname, category, and agent health. Store publication is a 0.2 deployment path.

Run `npm test` in this directory to enforce the hostname-only payload boundary.
