.PHONY: all backend frontend admin css test dev clean

all: frontend admin css backend

backend:
	cd src/backend && dotnet build

frontend:
	cd src/frontend/booking && elm make src/Main.elm --output=../../backend/wwwroot/booking.js

admin:
	cd src/frontend/admin && elm make src/Main.elm --output=../../backend/wwwroot/admin/admin.js

css:
	tailwindcss -i src/frontend/styles/booking.css -o src/backend/wwwroot/styles.css

test:
	cd tests/Michael.Tests && dotnet run

dev:
	overmind start

clean:
	rm -rf src/backend/bin src/backend/obj
	rm -rf tests/Michael.Tests/bin tests/Michael.Tests/obj
	rm -f src/backend/wwwroot/booking.js src/backend/wwwroot/admin/admin.js src/backend/wwwroot/styles.css
	rm -rf build
