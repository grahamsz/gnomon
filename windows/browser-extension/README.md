# Browser companion (Chrome and Edge)

The extension sends the active tab's **hostname only** to
`http://127.0.0.1:45981`. It does not collect or send URLs, paths, query strings,
titles, page content, or history.

For v1, open `chrome://extensions` or `edge://extensions`, enable Developer mode,
choose **Load unpacked**, and select this `extension` directory. Pin the extension
to see the current hostname, category, and agent health. Store publication is a
v1.1 deployment path; the MSI intentionally does not install the extension.

Run `npm test` in this directory to enforce the hostname-only payload boundary.
