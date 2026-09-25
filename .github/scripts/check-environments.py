"""Fails if the deployable environments are not listed identically everywhere they appear.

Adding an environment touches four places; this catches forgetting one of them.
"""

import pathlib
import re
import sys

root = pathlib.Path(__file__).resolve().parents[2]


def names(pattern: str, path: str) -> set[str]:
    text = (root / path).read_text()
    match = re.search(pattern, text, re.S)
    if not match:
        sys.exit(f"Could not find the environment list in {path}")
    return set(re.findall(r"[a-z]+", match.group(1)))


sources = {
    "infra/environments/*.tfvars": {p.stem for p in (root / "infra/environments").glob("*.tfvars")},
    "infra/variables.tf validation": names(
        r"contains\(\[([^\]]*)\], var\.environment\)", "infra/variables.tf"
    ),
    "infra/bootstrap/variables.tf default": names(
        r'variable "environments".*?default\s*=\s*\[([^\]]*)\]', "infra/bootstrap/variables.tf"
    ),
    ".github/workflows/deploy.yml options": names(
        r"options:\s*\[([^\]]*)\]", ".github/workflows/deploy.yml"
    ),
}

expected = sources["infra/environments/*.tfvars"]
mismatches = {source: envs for source, envs in sources.items() if envs != expected}

for source, envs in sources.items():
    print(f"{source:40} {', '.join(sorted(envs))}")

if mismatches:
    print("\nEnvironment lists are out of sync:", file=sys.stderr)
    for source, envs in mismatches.items():
        missing, extra = sorted(expected - envs), sorted(envs - expected)
        print(f"  {source}: missing {missing or '-'}, unexpected {extra or '-'}", file=sys.stderr)
    sys.exit(1)
