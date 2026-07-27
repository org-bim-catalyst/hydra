"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
require("datatables.net-bs5");
require("datatables.net-fixedheader-bs5");
require("datatables.net-responsive-bs5");
var datatables_net_fixedcolumns_bs5_1 = require("datatables.net-fixedcolumns-bs5");
//https://ralzohairi.medium.com/audio-recording-in-javascript-96eed45b75ee
//https://orangeable.com/javascript/equalizer-web-audio-api
//https://github.com/orangeable/javascript-equalizer/blob/master/js/main.js
//https://orangeable.com/javascript/equalizer-web-audio-api
//https://github.com/orangeable/javascript-equalizer
//https://openai.com/api/
//https://platform.openai.com/examples
//https://blog.teamtreehouse.com/getting-started-speech-synthesis-api#:~:text=To%20use%20a%20voice%2C%20set,speechSynthesis.
//https://developer.mozilla.org/en-US/docs/Web/API/SpeechSynthesis
//https://ourcodeworld.com/articles/read/405/how-to-convert-pdf-to-text-extract-text-from-pdf-with-javascript
//https://medium.com/@david.richards.tech/ai-audio-conversations-with-openai-whisper-3c730a9c7123
var app = /** @class */ (function () {
    function app() {
        this.initUi();
    }
    app.prototype.initUi = function () {
        //https://datatables.net/forums/discussion/53257/multiple-data-in-single-cell
        //https://datatables.net/examples/api/multi_filter_select.html
        var table = new datatables_net_fixedcolumns_bs5_1.default('#myTable', {
            ajax: {
                url: '/UsersManager/api/users',
                dataSrc: ''
            }, columns: [
                {
                    data: "profilePicture", orderable: false, width: '25%', className: 'dt-nowrap',
                    render: function (data, type, row) {
                        return "<img src=\"data:image/jpg;base64,".concat(row.profilePicture, "\" class=\"rounded-circle shadow-1-strong\" width=50 height=50> \n                                <br />\n                                <span><strong>Name: </strong> ").concat(row.firstName, " ").concat(row.lastName, "</span>\n                                <br />\n                                <span><strong>Id: </strong> ").concat(row.id, "</span>");
                    }
                },
                { data: "birthDate", orderable: true, width: '5%', className: 'dt-nowrap' },
                {
                    data: "userName", orderable: true, width: '5%',
                    render: function (data, type, row) {
                        return "<span>".concat(row.userName, "</span>\n                                <br />\n                                <span><strong>Normalized: </strong> ").concat(row.normalizedUserName, "</span>");
                    }
                },
                {
                    data: "email", orderable: true, width: '10%',
                    render: function (data, type, row) {
                        return "<span>".concat(row.email, "</span><span class=\"float-end ").concat(row.emailConfirmed ? 'text-primary' : 'text-warning', "\"><i class=\"").concat(row.emailConfirmed ? 'fas fa-check-circle' : 'fas fa-exclamation-circle', "\"></i></span>\n                                <br />\n                                <span><strong>Normalized: </strong> ").concat(row.normalizedEmail, "</span>");
                    }
                },
                { data: "passwordHash", orderable: true, width: '10%', className: 'dt-nowrap' },
                { data: "securityStamp", orderable: true, width: '10%', className: 'dt-nowrap' },
                { data: "concurrencyStamp", orderable: true, width: '10%', className: 'dt-nowrap' },
                {
                    data: "phoneNumber", orderable: true, width: '5%',
                    render: function (data, type, row) {
                        return "<span>".concat(row.phoneNumber, "</span><span class=\"float-end ").concat(row.phoneNumberConfirmed ? 'text-primary' : 'text-warning', "\"><i class=\"").concat(row.phoneNumberConfirmed ? 'fas fa-check-circle' : 'fas fa-exclamation-circle', "\"></i></span>");
                    }
                },
                {
                    data: "twoFactorEnabled", orderable: true, width: '5%',
                    render: function (data) {
                        return "<input class=\"form-check-input\" type=\"checkbox\"".concat(data ? 'checked' : '', " />");
                    }
                },
                { data: "lockoutEnd", orderable: true, width: '5%' },
                {
                    data: "lockoutEnabled", orderable: true, width: '5%',
                    render: function (data) {
                        return "<input class=\"form-check-input\" type=\"checkbox\" ".concat(data ? 'checked' : '', " />");
                    }
                },
                { data: "accessFailedCount", orderable: true, width: '5%' }
            ],
            order: [[1, 'asc']],
            fixedHeader: { header: true },
            responsive: {
                details: false
            },
            autoWidth: true,
            searching: true,
            fixedColumns: true,
            scrollX: true,
            paging: true
        });
    };
    return app;
}());
exports.default = app;
//# sourceMappingURL=app.js.map