# Settings for dmgbuild: what goes into IdleViz.dmg and how its Finder window looks.
# build-dmg.sh passes the built app as -D app=<path> and the picture as -D background=<path>.
import os.path

app = defines["app"]  # noqa: F821 (dmgbuild provides `defines`)
name = os.path.basename(app)

format = "UDZO"
files = [app]
symlinks = {"Applications": "/Applications"}
# The mounted volume shows the app's own icon.
icon = os.path.join(app, "Contents", "Resources", "AppIcon.icns")

background = defines["background"]  # noqa: F821
window_rect = ((200, 200), (600, 400))
default_view = "icon-view"
show_status_bar = False
show_tab_view = False
show_toolbar = False
show_pathbar = False
show_sidebar = False
icon_size = 128
text_size = 13
# Where make-assets.swift draws the arrow between them.
icon_locations = {name: (150, 170), "Applications": (450, 170)}
