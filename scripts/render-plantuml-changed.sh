#!/usr/bin/env bash
set -euo pipefail

app_dir="${1:-swa/hello-portal}"
source_dir="$app_dir/assets/plantuml"
output_dir="$app_dir/static/diagrams"
plantuml_jar="${PLANTUML_JAR:-/tmp/plantuml.jar}"

if [ ! -d "$source_dir" ]; then
  echo "No PlantUML source directory found at $source_dir"
  exit 0
fi

if ! java -version >/dev/null 2>&1; then
  echo "Java is required to render PlantUML diagrams. Install a JRE locally or use the GitHub Actions workflow with setup-java." >&2
  exit 1
fi

if [ ! -f "$plantuml_jar" ]; then
  curl -fsSL -o "$plantuml_jar" \
    https://github.com/plantuml/plantuml/releases/latest/download/plantuml.jar
fi

mkdir -p "$output_dir"

changed_file="$(mktemp)"
selected_file="$(mktemp)"
trap 'rm -f "$changed_file" "$selected_file"' EXIT

git diff --name-only --diff-filter=ACMRT HEAD^ HEAD -- "$source_dir" 2>/dev/null |
  awk '/\.puml$/' > "$changed_file"
git diff --name-only --diff-filter=ACMRT HEAD -- "$source_dir" 2>/dev/null |
  awk '/\.puml$/' >> "$changed_file"

cat "$changed_file" > "$selected_file"

find "$source_dir" -type f -name '*.puml' | sort | while IFS= read -r source; do
  relative="${source#"$source_dir"/}"
  target="$output_dir/${relative%.puml}.png"
  if [ ! -f "$target" ]; then
    echo "$source" >> "$selected_file"
  fi
done

sort -u "$selected_file" -o "$selected_file"

if [ ! -s "$selected_file" ]; then
  echo "No changed PlantUML files and no missing rendered diagrams."
  exit 0
fi

while IFS= read -r source; do
  relative="${source#"$source_dir"/}"
  target_subdir="$(dirname "$relative")"
  if [ "$target_subdir" = "." ]; then
    target_subdir=""
  fi
  mkdir -p "$output_dir/$target_subdir"
  echo "Rendering $source"
  java -jar "$plantuml_jar" -tpng -o "$(pwd)/$output_dir/$target_subdir" "$source"
done < "$selected_file"
