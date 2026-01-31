tailwind: tailwindcss -i src/frontend/styles/booking.css -o src/backend/wwwroot/styles.css --watch=always
elm: cd src/frontend/booking && elm make src/Main.elm --output=../../backend/wwwroot/booking.js && inotifywait -m -r -e modify,create,delete src/ | while read -r; do sleep 0.3; elm make src/Main.elm --output=../../backend/wwwroot/booking.js; done
backend: dotnet watch --no-hot-reload run --project src/backend/Michael.fsproj
