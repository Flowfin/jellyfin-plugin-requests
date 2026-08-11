/*
 * What both plugin pages share: the strip of links between them, the stylesheet load, and the small
 * amount of code that talks to this plugin's API.
 *
 * It is a global object rather than a module. A plugin page is injected into the dashboard's own
 * document rather than loaded as a document of its own, so `import` has no base to resolve against
 * and a module graph would be a second loader inside a page the dashboard already loaded. This is
 * the shape the server's own configuration pages use.
 *
 * Nothing here is a framework and nothing is fetched from anywhere. The two things it uses,
 * `ApiClient` and `Dashboard`, are the dashboard's own and are already on the page.
 */

var RequestsShell = {
    /*
     * Where this plugin's API lives. The version segment is part of the path on purpose, so a caller
     * pinned to it keeps the shape it expects; the rule for changing it is docs/api.md. It is written
     * once here because a page that spells its own base path is a page that keeps a stale one.
     */
    apiBase: "MediaRequests/v1",

    /*
     * The pages, in the order the strip shows them. The queue is first because it is the one an
     * operator opens every day and the settings are the ones they open twice.
     */
    pages: [
        { name: "RequestsQueue", label: "Queue" },
        { name: "Requests", label: "Settings" },
    ],

    /*
     * Puts the shell on a page and returns when it is there.
     *
     * `current` is the registered page name of the page calling this, which is what decides the link
     * that is marked rather than the page working it out from the address: the dashboard's address is
     * a hash route and reading it means parsing one, which is a second place the page names can drift
     * apart.
     */
    attach: function (page, current) {
        RequestsShell.loadStyle();

        var host = page.querySelector(".requestsShell");

        if (!host) {
            return;
        }

        var nav = document.createElement("nav");
        nav.className = "requestsShellNav";
        nav.setAttribute("aria-label", "Requests");

        RequestsShell.pages.forEach(function (entry) {
            var link = document.createElement("a");

            /*
             * A hash route rather than a page address. The dashboard is one document and its router
             * answers a hash change by swapping the content, so this moves between the two pages without
             * the reload that would throw away whatever the operator had open.
             */
            link.href = "#/configurationpage?name=" + encodeURIComponent(entry.name);
            link.textContent = entry.label;

            if (entry.name === current) {
                link.setAttribute("aria-current", "page");
            }

            nav.appendChild(link);
        });

        host.replaceChildren(nav);
    },

    /*
     * The shared stylesheet, loaded once however many pages ask for it. Each plugin page is fetched
     * on its own, so without the guard a move between the two adds a second copy of the same link
     * element every time.
     */
    loadStyle: function () {
        if (document.querySelector("link[data-requests-shell]")) {
            return;
        }

        // Named `sheet` rather than `link`, because `link` is what the strip's anchors are called
        // above and EveryLinkTheStripDrawsIsAHashRoute reads those assignments out of this file. Two
        // different things under one name would make that check read a stylesheet address as a
        // navigation target.
        var sheet = document.createElement("link");
        sheet.rel = "stylesheet";
        sheet.type = "text/css";
        sheet.dataset.requestsShell = "true";
        sheet.href = ApiClient.getUrl("web/ConfigurationPage", { name: "Requests.css" });

        document.head.appendChild(sheet);
    },

    /*
     * A read from this plugin's API, with the session the dashboard is already signed in with.
     *
     * `ApiClient.ajax` rather than `fetch`, because the endpoints carry a policy and none of them is
     * reachable without a session: the client is what holds the token and adds the header, and a bare
     * fetch would be a second place that has to know how this server authenticates.
     *
     * No page calls this yet. Every endpoint answers 500 on a real server today, because the policy
     * the controller names is not one the server registers; that is #51's and it is measured rather
     * than supposed. This is here so the pages that read the queue are written against one helper
     * rather than each growing its own.
     */
    get: function (path) {
        return ApiClient.ajax({
            type: "GET",
            url: ApiClient.getUrl(RequestsShell.apiBase + "/" + path),
            dataType: "json",
        });
    },

    /*
     * A write to this plugin's API, under the same session.
     */
    post: function (path, body) {
        return ApiClient.ajax({
            type: "POST",
            url: ApiClient.getUrl(RequestsShell.apiBase + "/" + path),
            data: JSON.stringify(body || {}),
            contentType: "application/json",
            dataType: "json",
        });
    },

    /*
     * Puts a sentence where the operator is looking.
     *
     * `textContent` rather than any of the ways of writing markup. What is shown here can carry a
     * message the server built out of text a person typed, and rendering that as markup is a
     * scripting hole in the dashboard of a server reachable by anybody who can file a request.
     */
    say: function (page, text) {
        var target = page.querySelector(".requestsMessage");

        if (target) {
            target.textContent = text;
        }
    },
};
