# Testing

## Does the plugin load on a real server

A plugin that builds is not a plugin that loads. An ABI mismatch, an embedded resource path that
stopped resolving after a rename, and a dependency the host does not provide all build clean and
fail at server start or on first use. The suite cannot see any of the three, because it has no
server.

`scripts/verify-plugin-loads.sh` starts a server of one claimed line in a container, installs the
plugin built for that line's runtime, and asks the server itself what it has. It ends non-zero
unless the server reports the plugin as `Active`.

    scripts/verify-plugin-loads.sh jellyfin/jellyfin:10.11.11 net9.0  18096
    scripts/verify-plugin-loads.sh jellyfin/jellyfin:12.0-rc4  net10.0 18097

Run them one after the other rather than together: each publishes into the same tree and each wants
its own port, and the second argument has to be the target framework the image's runtime provides.

What it needs is a container engine, a .NET SDK, `curl` and `python3`. What it does not need is a
display, administrator rights or a trusted certificate. The server is reached over plain HTTP on
the loopback interface, the container is removed when the run ends, and the one account it creates
lives no longer than the container. Nothing it runs can raise a prompt on the machine it runs on.

The plugin is copied into the container after the server has started, because `/config` is a volume
and a copy made before that lands where the running server never looks. The server is then
restarted, because plugins are read at start.

### The recorded run

Run on `e574918775c69640139ee1ecc1f2202efeff27aa`, 2026-08-06, against these images:

    jellyfin/jellyfin@sha256:aefb67e6a7ff1debdd154a78a7bbb780fd0c873d8639210a7f6a2016ad2b35db
    jellyfin/jellyfin@sha256:db1df1d111c27ba1f10bb8fce6630892f66eb66b12c2b24e79011453ac18b3db

The 10.11 line, `scripts/verify-plugin-loads.sh jellyfin/jellyfin:10.11.11 net9.0 18096`:

    == wait for the server to answer
    {"LocalAddress":"http://172.17.0.2:8096","ServerName":"f12222e96abc","Version":"10.11.11","ProductName":"Jellyfin Server","OperatingSystem":"","Id":"5b159e1a1d02497997cf1f933901aafe","StartupWizardCompleted":false}

    == what the server says about its plugins
    Name=AudioDB  Version=10.11.11.0  Status=Active  Id=a629c0dafac54c7e931a7174223f14c8
    Name=MusicBrainz  Version=10.11.11.0  Status=Active  Id=8c95c4d2e50c4fb0a4f36c06ff0f9a1a
    Name=OMDb  Version=10.11.11.0  Status=Active  Id=a628c0dafac54c7e9d1a7134223f14c8
    Name=Requests  Version=1.0.0.0  Status=Active  Id=eb5d78948eef4b36aa6f5d124e828ce1
    Name=Studio Images  Version=10.11.11.0  Status=Active  Id=872a78491171458da6fb3de3d442ad30
    Name=TMDb  Version=10.11.11.0  Status=Active  Id=b8715ed16c4745289ad3f72deb539cd4

    == verdict
    Requests is Active, id eb5d78948eef4b36aa6f5d124e828ce1

    == the configuration page the dashboard would fetch
    GET /web/ConfigurationPage?name=Requests -> 200
    <!doctype html>
    <html lang="en">
        <head>
            <meta charset="utf-8" />
            <title>Requests</title>

    == the configuration the page reads and writes
    {}
    POST /Plugins/<id>/Configuration -> 204

    == done
    Requests loaded and answered on jellyfin/jellyfin:10.11.11 (net9.0)

The 12.0 line, `scripts/verify-plugin-loads.sh jellyfin/jellyfin:12.0-rc4 net10.0 18097`:

    == wait for the server to answer
    {"LocalAddress":"http://172.17.0.2:8096","ServerName":"2461813535d0","Version":"12.0.0","ProductName":"Jellyfin Server","OperatingSystem":"","Id":"2e7c15bb59eb43ad946f7e839158e207","StartupWizardCompleted":false}

    == what the server says about its plugins
    Name=AudioDB  Version=12.0.0.0  Status=Active  Id=a629c0dafac54c7e931a7174223f14c8
    Name=ListenBrainz Similarity Provider  Version=12.0.0.0  Status=Active  Id=a5b2e8c19d4f4a3b8c7e6f1a2b3c4d5e
    Name=MusicBrainz  Version=12.0.0.0  Status=Active  Id=8c95c4d2e50c4fb0a4f36c06ff0f9a1a
    Name=OMDb  Version=12.0.0.0  Status=Active  Id=a628c0dafac54c7e9d1a7134223f14c8
    Name=Requests  Version=1.0.0.0  Status=Active  Id=eb5d78948eef4b36aa6f5d124e828ce1
    Name=Studio Images  Version=12.0.0.0  Status=Active  Id=872a78491171458da6fb3de3d442ad30
    Name=TMDb  Version=12.0.0.0  Status=Active  Id=b8715ed16c4745289ad3f72deb539cd4

    == verdict
    Requests is Active, id eb5d78948eef4b36aa6f5d124e828ce1

    == the configuration page the dashboard would fetch
    GET /web/ConfigurationPage?name=Requests -> 200

    == the configuration the page reads and writes
    {}
    POST /Plugins/<id>/Configuration -> 204

    == done
    Requests loaded and answered on jellyfin/jellyfin:12.0-rc4 (net10.0)

### That the check bites

A procedure that cannot fail proves nothing, so the mismatch it exists to catch was fed to it. The
12.0 build, which compiles and packages cleanly, installed on a 10.11 server:

    scripts/verify-plugin-loads.sh jellyfin/jellyfin:10.11.11 net10.0 18098

    == what the server says about its plugins
    Name=AudioDB  Version=10.11.11.0  Status=Active  Id=a629c0dafac54c7e931a7174223f14c8
    Name=Jellyfin.Plugin.Requests  Version=10.11.11.0  Status=NotSupported  Id=ff768900c0ec47d72192ea5c133ecc62
    Name=MusicBrainz  Version=10.11.11.0  Status=Active  Id=8c95c4d2e50c4fb0a4f36c06ff0f9a1a
    Name=OMDb  Version=10.11.11.0  Status=Active  Id=a628c0dafac54c7e9d1a7134223f14c8
    Name=Studio Images  Version=10.11.11.0  Status=Active  Id=872a78491171458da6fb3de3d442ad30
    Name=TMDb  Version=10.11.11.0  Status=Active  Id=b8715ed16c4745289ad3f72deb539cd4

    == verdict
    Requests is not in the plugin list: the server did not load it.
    exit=1

The server keeps the plugin in its list under the file name rather than the plugin's own name, with
`Status=NotSupported`, because it never got far enough to ask the plugin what it is called. That is
the shape a reader should expect when this fails.

### What this does not cover

The run above happened on one machine, by hand. Nothing on a merge route runs it, so a change that
breaks loading reaches the mainline the same way a change that does not.

The 12.0 line is a release candidate. `12.0-rc4` is what exists today and the tag will move.

The packages are not what a catalogue would serve. This installs the assembly the build produces
into the plugin directory; it does not build the zip, publish a manifest, or exercise the install
path an operator uses. Those are M14.

Nothing here plays media, so the container's transcoding dependencies are never touched, and a
failure that needs a real library to show would not show here.
