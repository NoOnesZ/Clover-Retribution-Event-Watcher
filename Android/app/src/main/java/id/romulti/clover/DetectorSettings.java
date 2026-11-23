package id.romulti.clover;

import android.content.Context;
import android.content.SharedPreferences;

final class DetectorSettings {
    static final String WEBHOOK = "webhook";
    static final String PRIVATE_SERVER = "private_server_code";
    static final String DISCORD_USER_ID = "discord_user_id";
    static final String REGION = "region";
    static final String COMMON = "common";
    static final String STATUS = "status";
    static final String LAST_EVENT = "last_event";
    static final String DELIVERY = "delivery";

    static SharedPreferences get(Context context) {
        return context.getSharedPreferences("detector", Context.MODE_PRIVATE);
    }

    private DetectorSettings() {}
}
