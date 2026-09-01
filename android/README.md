# Gnomon Android agent

Open this directory in current Android Studio, install Android API 37, JDK 17, and
Gradle 9.5, then build the `app` module. Gnomon requires Android 8 (API 26) or later.

The app explains and links to Usage Access, battery-optimization exemption, and
notification permission before tracking. The optional notification-listener media
permission is not required because 0.1's explicit Android rule is simply **screen
on + mapped foreground app**. Room was chosen over JSON DataStore because queued
deltas need transactional FIFO deletion and a strict 720-row cap.

Run unit tests with `./gradlew test` and the usage-event reducer instrumentation
test with `./gradlew connectedAndroidTest`.

Multi-user limitation: 0.1 tracks only the Android profile in which it is installed
and running. Guest and secondary profiles bypass it and are a parent setup concern.
The app has no accessibility service, overlay, package suspension, analytics, crash
reporting, or network destination other than the configured Home Assistant host.
