.PHONY: all backend frontend admin css test dev clean e2e e2e-safe e2e-api e2e-booking e2e-install

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

e2e-install:
	cd tests/e2e && npm install

e2e: e2e-install
	cd tests/e2e && npx vitest run api/ && npx playwright test --pass-with-no-tests

e2e-safe: e2e-install
	cd tests/e2e && MICHAEL_TEST_MODE=safe npx vitest run api/ && MICHAEL_TEST_MODE=safe npx playwright test --pass-with-no-tests

e2e-api: e2e-install
	cd tests/e2e && npx vitest run api/

e2e-booking: e2e-install
	cd tests/e2e && npx playwright test --pass-with-no-tests

clean:
	rm -rf src/backend/bin src/backend/obj
	rm -rf tests/Michael.Tests/bin tests/Michael.Tests/obj
	rm -f src/backend/wwwroot/booking.js src/backend/wwwroot/admin/admin.js src/backend/wwwroot/styles.css
	rm -rf build
