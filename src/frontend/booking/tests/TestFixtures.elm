module TestFixtures exposing
    ( sampleParseResponse
    , sampleSlot
    , sampleWindow
    , validToken
    )

import Types


validToken : String
validToken =
    "1234567890:aabbccdd11223344aabbccdd11223344:aabbccdd11223344aabbccdd11223344aabbccdd11223344aabbccdd11223344"


sampleWindow : Types.AvailabilityWindow
sampleWindow =
    { start = "2026-03-10T09:00:00-05:00"
    , end = "2026-03-10T11:00:00-05:00"
    , timezone = Just "America/New_York"
    }


sampleSlot : Types.TimeSlot
sampleSlot =
    { start = "2026-03-10T13:00:00-05:00"
    , end = "2026-03-10T13:30:00-05:00"
    }


sampleParseResponse : Types.ParseResponse
sampleParseResponse =
    { parseResult =
        { availabilityWindows = [ sampleWindow ]
        , durationMinutes = Just 30
        , title = Nothing
        , description = Nothing
        , name = Nothing
        , email = Nothing
        , phone = Nothing
        , missingFields = []
        }
    , systemMessage = "ok"
    }
