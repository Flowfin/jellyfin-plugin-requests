/*
 * What both plugin pages share: the strip of links between them, the stylesheet load, the words
 * they draw, and the small amount of code that talks to this plugin's API.
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
     * operator opens every day and the settings are the ones they open twice. Each carries the key
     * its label is under rather than the label, which is #73: no word a person reads is written in
     * a file this plugin ships as code.
     */
    pages: [
        { name: "RequestsQueue", label: "shell.page.RequestsQueue" },
        { name: "Requests", label: "shell.page.Requests" },
    ],

    /*
     * The words, once they have arrived. Empty until then, and a lookup against an empty catalogue
     * answers with nothing rather than with a key, because showing somebody `queue.column.title` is
     * showing them the inside of the plugin.
     *
     * What that costs, stated rather than left to be found: a catalogue that cannot be fetched
     * leaves a page wordless, the sentence that would say so included. It is not a failure of its
     * own, because the catalogue and the queue are two calls to the same server behind the same
     * session, so a page that cannot reach one cannot reach the other.
     */
    words: {},

    /*
     * The catalogue, fetched once however many pages ask for it. The culture is the browser's,
     * handed over rather than left to the request headers, because the dashboard's client sets its
     * own and a person who changed the language in Jellyfin has changed the one `navigator.language`
     * answers with.
     */
    load: function () {
        if (Object.keys(RequestsShell.words).length > 0) {
            return Promise.resolve();
        }

        var asked = window.navigator && window.navigator.language ? { culture: window.navigator.language } : {};

        return RequestsShell.get("Strings", asked).then(function (answer) {
            RequestsShell.words = answer.Strings || {};
        });
    },

    /*
     * One word, by key.
     */
    word: function (key) {
        return Object.prototype.hasOwnProperty.call(RequestsShell.words, key) ? RequestsShell.words[key] : "";
    },

    /*
     * A value the API answered with, as a word. A value no key exists for is shown as it came
     * rather than dropped, so a state added to the model appears as its own name instead of as a
     * blank cell.
     */
    named: function (group, value) {
        return RequestsShell.word(group + "." + value) || value;
    },

    /*
     * A sentence with values put into it, where the catalogue holds the sentence and this holds
     * none of it. The placeholders are numbered rather than named so a translator can move them,
     * which is the whole reason a sentence is one string instead of three pieces joined here.
     */
    fill: function (key, values) {
        return RequestsShell.word(key).replace(/\{(\d+)\}/g, function (whole, at) {
            var value = values[Number(at)];

            return value === undefined ? whole : String(value);
        });
    },

    /*
     * Every element on a page that names a key gets the word under it.
     */
    draw: function (page) {
        page.querySelectorAll("[data-i18n]").forEach(function (element) {
            element.textContent = RequestsShell.word(element.getAttribute("data-i18n"));
        });
    },

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
        nav.setAttribute("aria-label", RequestsShell.word("shell.label"));

        RequestsShell.pages.forEach(function (entry) {
            var link = document.createElement("a");

            /*
             * A hash route rather than a page address. The dashboard is one document and its router
             * answers a hash change by swapping the content, so this moves between the two pages without
             * the reload that would throw away whatever the operator had open.
             */
            link.href = "#/configurationpage?name=" + encodeURIComponent(entry.name);
            link.textContent = RequestsShell.word(entry.label);

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
     * The question goes in `asked` and is turned into a query string by the client rather than by
     * the caller. A page that built its own would be a second place that has to know how a value is
     * escaped, and the queue's filters carry text somebody typed.
     */
    get: function (path, asked) {
        return ApiClient.ajax({
            type: "GET",
            url: ApiClient.getUrl(RequestsShell.apiBase + "/" + path, asked),
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
     * Puts a sentence where the operator is looking, named by its key rather than written by the
     * caller. An empty key clears it.
     *
     * Values are optional and go through `fill`, so a sentence about one request says which one.
     * The alternative is a caller joining a word to a value itself, which is a sentence built in
     * the page out of pieces no translator can reorder.
     *
     * `textContent` rather than any of the ways of writing markup. What is shown here can carry a
     * message the server built out of text a person typed, and rendering that as markup is a
     * scripting hole in the dashboard of a server reachable by anybody who can file a request.
     */
    say: function (page, key, values) {
        var target = page.querySelector(".requestsMessage");

        if (!target) {
            return;
        }

        if (!key) {
            target.textContent = "";
            return;
        }

        target.textContent = values ? RequestsShell.fill(key, values) : RequestsShell.word(key);
    },
};
