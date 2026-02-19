tailwind: tailwindcss -i src/frontend/styles/booking.css -o src/backend/wwwroot/styles.css --watch=always
elm: cd src/frontend/booking && elm make src/Main.elm --output=../../backend/wwwroot/booking.js && inotifywait -m -r -e modify,create,delete src/ | while read -r; do sleep 0.3; elm make src/Main.elm --output=../../backend/wwwroot/booking.js; done
admin-elm: cd src/frontend/admin && elm make src/Main.elm --output=../../backend/wwwroot/admin/admin.js && inotifywait -m -r -e modify,create,delete src/ | while read -r; do sleep 0.3; elm make src/Main.elm --output=../../backend/wwwroot/admin/admin.js; done
fake-caldav: dotnet run --project src/fake-caldav/FakeCalDav.fsproj
mailpit: mailpit --smtp=0.0.0.0:1025 --listen=0.0.0.0:8025
backend: dotnet watch --no-hot-reload run --project src/backend/Michael.fsproj
