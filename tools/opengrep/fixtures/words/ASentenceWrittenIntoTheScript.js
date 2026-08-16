/*
 * Fixture for page-sets-no-text-from-a-literal and for
 * control-is-named-from-the-catalogue. This file is embedded in nothing and
 * served to nobody; it exists so the two rules can be watched refusing the
 * mistakes they name.
 *
 * The near-miss is what somebody writes while making a failure friendlier. The
 * page already had an empty state, the sentence in it was thin, and improving it
 * means typing a better one where the old one was. Nothing about the result
 * looks wrong, and the improved sentence is the one string on the page no
 * language file can reach.
 */

var RequestsFixture = {
    // Legal neighbour: the word comes from the catalogue and the literal on this
    // line is a key rather than something a person reads.
    empty: function (summary) {
        summary.textContent = RequestsShell.word("mine.empty");
    },

    // Legal neighbour: cleared rather than written, which carries no word.
    clear: function (target) {
        target.textContent = "";
    },

    // The regression, one improvement later.
    friendlier: function (summary) {
        summary.textContent = "You have not asked for anything yet.";
    },

    // Legal neighbour: the strip's own name, from the catalogue.
    label: function (nav) {
        nav.setAttribute("aria-label", RequestsShell.word("shell.label"));
    },

    // The regression a screen reader meets and nobody else does.
    named: function (nav) {
        nav.setAttribute("aria-label", "Requests");
    },
};
