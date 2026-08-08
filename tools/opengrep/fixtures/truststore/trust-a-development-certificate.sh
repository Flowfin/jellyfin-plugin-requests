#!/usr/bin/env bash
# Fixture for no-certificate-store-access, in its command-line spelling. Nothing
# runs this file; it exists so the rule can be watched refusing the mistake it
# names.
#
# The near-miss is the setup step somebody adds to make a suite pass on their own
# machine, copied out of an ASP.NET getting-started page where it is the right
# advice. Both lines below raise a consent prompt on the machine that runs them,
# which is what makes this a rule about the person sitting at the machine rather
# than about the test.
set -euo pipefail

# Legal neighbours, left here on purpose: the suite is this and nothing else, and
# the rule has to stay quiet on both.
dotnet restore Jellyfin.Plugin.Requests.sln
dotnet test Jellyfin.Plugin.Requests.sln --configuration Release

# The regression, in both spellings the platforms use.
dotnet dev-certs https --trust
certutil -addstore -user Root test-certificate.cer
