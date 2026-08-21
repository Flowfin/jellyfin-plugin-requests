# The collision scan #119 asks for, over what two runs of a server declared.
#
# It is a file of its own rather than a block inside the harness for one reason: a scan that has
# never refused anything is a claim. Held here, it can be run over fixtures written to collide, and
# `scripts/prove-collision-scan.sh` runs it over one per collision kind and over a clean set, with no
# container and no server. The harness calls exactly this file, so what the fixtures prove is what
# the matrix runs.
#
# usage: python3 scripts/sibling-collision-scan.py <alone-dir> <together-dir> <installed-file>
#
# Each directory holds what one run of a server declared, written by the harness:
#
#   paths.txt     one route path per line, from the server's own OpenAPI document
#   tasks.txt     one scheduled task per line, key and name separated by a tab
#   configs.txt   one configuration file name per line, from the plugin configuration directory
#   plugins.txt   one plugin per line: name, version, status and identifier, tab separated
#
# Exit 0 with the finding printed, or 1 with one line per collision.
import io
import sys

MINE = "MediaRequests"
MY_NAME = "Requests"


def lines(path):
    """The non-empty lines of a file, or none where the file is not there."""
    try:
        return [line for line in io.open(path, encoding="utf-8").read().splitlines() if line.strip()]
    except IOError:
        return []


def duplicates(values):
    """Every value that appears more than once, in the order it first repeats."""
    seen, twice = set(), []
    for value in values:
        if value in seen and value not in twice:
            twice.append(value)
        seen.add(value)
    return twice


def scan(alone, together):
    """Every collision between the two runs, as sentences somebody can act on."""
    problems = []

    # EVERY PLUGIN THE SECOND RUN LISTS HAS TO BE RUNNING. A sibling the server refused is not an
    # interoperability result on its own, and it is reported rather than swallowed: a set that half
    # installed would otherwise produce a green scan over a server running one plugin.
    rows = [row.split("\t") for row in lines(together + "/plugins.txt")]

    for row in rows:
        if len(row) > 2 and row[2] != "Active":
            problems.append("{0} is {1} rather than Active with the set installed".format(row[0], row[2]))

    if not any(row[0] == MY_NAME for row in rows):
        problems.append("this plugin is not in the list with the set installed")

    # ROUTES, COMPARED BETWEEN THE TWO RUNS RATHER THAN INSIDE ONE. A repeat inside one document
    # cannot be found: `paths` is a JSON object, so two declarations of one path are one key by the
    # time anything can read it, and a check looking for a repeat there could never fire. What a
    # route taken by a sibling actually looks like is the path of this plugin disappearing, and that
    # is visible only against the run where it was there.
    alone_mine = sorted(p for p in lines(alone + "/paths.txt") if MINE in p)
    together_mine = sorted(p for p in lines(together + "/paths.txt") if MINE in p)

    if not alone_mine:
        problems.append("this plugin declared no route even alone, so the comparison is over nothing")

    for path in sorted(set(alone_mine) - set(together_mine)):
        problems.append("the route {0} is served alone and not beside the set".format(path))

    for path in sorted(set(together_mine) - set(alone_mine)):
        problems.append("the route {0} appears only beside the set".format(path))

    # SCHEDULED TASKS. A list rather than an object, so a repeat is readable where it happened. The
    # key is what the server stores a trigger against and the name is what an operator reads, so a
    # repeat of either is a collision even where the other differs.
    tasks = [row.split("\t") for row in lines(together + "/tasks.txt")]

    for key in duplicates([row[0] for row in tasks]):
        problems.append("two scheduled tasks under the key {0}".format(key))

    for name in duplicates([row[1] for row in tasks if len(row) > 1]):
        problems.append("two scheduled tasks named {0}".format(name))

    # CONFIGURATION. The directory listing is evidence and not the check, for the same reason as the
    # routes: a directory cannot hold two files under one name, so by the time the collision has
    # happened it is invisible there. What decides the file is the plugin, so two plugins under one
    # name is the fight, and a repeated identifier is the same fight one step further, since the
    # server keys a plugin by it.
    for name in duplicates([row[0] for row in rows]):
        problems.append("two plugins named {0}, which is one configuration file between them".format(name))

    for identifier in duplicates([row[3] for row in rows if len(row) > 3]):
        problems.append("two plugins under the identifier {0}".format(identifier))

    # A configuration file that was there alone and is not there beside the set is a plugin whose
    # settings another one wrote over.
    for name in sorted(set(lines(alone + "/configs.txt")) - set(lines(together + "/configs.txt"))):
        problems.append("the configuration file {0} is there alone and gone beside the set".format(name))

    return problems


def main(argv):
    if len(argv) != 4:
        raise SystemExit("usage: sibling-collision-scan.py <alone-dir> <together-dir> <installed-file>")

    alone, together, installed = argv[1], argv[2], argv[3]

    print("the set, per board:")
    for row in lines(installed):
        print("  " + row)

    problems = scan(alone, together)

    if problems:
        for problem in problems:
            print("COLLISION: " + problem)
        raise SystemExit("{0} collision(s).".format(len(problems)))

    print("no collision over routes, scheduled task names and keys, or plugin configuration")


if __name__ == "__main__":
    main(sys.argv)
