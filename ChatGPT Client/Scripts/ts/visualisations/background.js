"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
var d3 = require("d3");
var VizBackground = /** @class */ (function () {
    function VizBackground(_selector) {
        var svg = d3.select(_selector);
        svg.attr('background-color', 'white');
        svg.selectAll('*').remove();
        var margin = {
            top: 0,
            right: 0,
            bottom: 0,
            left: 0
        };
        var width = +svg.attr('width') - margin.left - margin.right;
        var height = +svg.attr('height') - margin.top - margin.bottom;
        this.svg = svg;
    }
    VizBackground.prototype.visualize = function (data) {
        // content area of your visualization
        var vis = this.svg.append('g');
        vis.append('rect');
        var scale = d3.scaleLinear()
            .range([20, 90])
            .domain([-256, 0]);
        var hueScale = d3.scaleLinear()
            .range([250, 200])
            .domain([-120, -50]);
        var s = this.reduceArray(data.slice(0, 3));
        var h = this.reduceArray(data.slice(0, Math.floor(data.length / 3)));
        var l = this.reduceArray(data.slice(data));
        vis.select("rect")
            .attr("fill", "hsl(" + (hueScale(h)) + ", "
            + scale(s) + "%,"
            + scale(l) + "%)");
        this.debouncer(this.lightModeDebouncer(this.svg, hueScale(h), scale(s), scale(l)), 500);
        vis.select("rect")
            .attr("width", vis.attr("width"))
            .attr("height", vis.attr("height"));
    };
    VizBackground.prototype.lightModeDebouncer = function (svg, h, s, l) {
        if (l > 50) {
            return svg.attr("class", "light");
        }
        else {
            return svg.attr("class", "dark");
        }
    };
    VizBackground.prototype.reduceArray = function (d) {
        return d.reduce(function (a, x) {
            return a + x;
        }, 0) / d.length;
    };
    VizBackground.prototype.debouncer = function (callback, intervalSize) {
        var timeout;
        return function () {
            if (timeout) {
                return;
            }
            timeout = window.setTimeout(function () {
                timeout = null;
            }, intervalSize);
            callback.apply(this, arguments);
        };
    };
    return VizBackground;
}());
exports.default = VizBackground;
//# sourceMappingURL=background.js.map